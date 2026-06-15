using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace StartWithAspect
{
    // ============================================================
    //  StartWithAspect
    //  Choisir un aspect d'elite de depart depuis la fenetre "Arsenal".
    //
    //  - Une ligne "ASPECT" est ajoutee dans l'Arsenal (selection de perso) :
    //    une icone cliquable par aspect (tous detectes automatiquement, DLC inclus)
    //    + un bouton "aucun".
    //  - Au debut de la partie, le joueur recoit cet aspect dans son slot
    //    d'equipement (comportement vanilla : il devient cet elite).
    //  - Multijoueur : chaque joueur transmet son choix au serveur, qui donne
    //    a chacun son propre aspect.
    //  - Un reglage de config "Aspect de depart" sert aussi de selecteur de secours.
    // ============================================================
    [BepInDependency(NetworkingAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ExamplePlugin : BaseUnityPlugin
    {
        public const string PluginGUID    = PluginAuthor + "." + PluginName;
        public const string PluginAuthor  = "virgile";
        public const string PluginName    = "StartWithAspect";
        public const string PluginVersion = "1.0.1";

        // Selecteur temporaire : nom de l'aspect choisi (lu dans le .cfg).
        private static ConfigEntry<string> chosenAspect;

        // Joueurs deja servis dans la partie en cours (pour ne donner qu'une fois).
        private static readonly HashSet<CharacterMaster> grantedThisRun =
            new HashSet<CharacterMaster>();

        // PHASE 3 (multi) : choix d'aspect de chaque joueur, recu cote serveur,
        // indexe par l'identifiant reseau (netId) du NetworkUser du joueur.
        private static readonly Dictionary<NetworkInstanceId, string> aspectByUser =
            new Dictionary<NetworkInstanceId, string>();

        public void Awake()
        {
            Log.Init(Logger);

            chosenAspect = Config.Bind(
                "Aspect",
                "Aspect de depart",
                "",
                "Nom interne de l'aspect a donner au debut de la partie (ex: EliteFireEquipment). " +
                "Laisse vide pour n'en donner aucun. La liste exacte des noms valides est " +
                "affichee dans la console BepInEx au lancement d'une partie.");

            // Debut d'une nouvelle partie : on remet le suivi a zero et on logge
            // la liste des aspects disponibles dans cette installation.
            Run.onRunStartGlobal += OnRunStart;

            // Apparition d'un corps de personnage : on tente de donner l'aspect.
            On.RoR2.CharacterMaster.OnBodyStart += OnBodyStart;

            // PHASE 2 : quand le panneau Arsenal se construit, on ajoute notre ligne.
            On.RoR2.UI.LoadoutPanelController.Rebuild += OnLoadoutRebuild;

            // PHASE 3 (multi) : on enregistre le message reseau, et a l'entree en
            // selection de perso chaque client envoie son choix au serveur.
            NetworkingAPI.RegisterMessageType<SetAspectMessage>();
            On.RoR2.UI.CharacterSelectController.Awake += OnCharacterSelectAwake;

            Log.Info($"{PluginName} v{PluginVersion} charge !");
        }

        private void OnRunStart(Run run)
        {
            grantedThisRun.Clear();
            LogAvailableAspects();
        }

        private void OnBodyStart(
            On.RoR2.CharacterMaster.orig_OnBodyStart orig,
            CharacterMaster self,
            CharacterBody body)
        {
            orig(self, body);

            // On agit uniquement cote serveur (= l'hote). C'est lui qui possede les
            // choix de tous les joueurs (recus par message reseau) et donne l'equipement.
            if (!NetworkServer.active)
                return;

            // Uniquement les personnages controles par un joueur.
            if (self == null || self.playerCharacterMasterController == null)
                return;

            // Une seule fois par joueur et par partie.
            if (grantedThisRun.Contains(self))
                return;

            // Choix propre a CE joueur (et non un choix global).
            string wantedName = GetChoiceForMaster(self);
            EquipmentIndex aspect = FindAspectByName(wantedName);
            if (aspect == EquipmentIndex.None)
                return;

            if (self.inventory != null)
            {
                self.inventory.SetEquipmentIndex(aspect);
                grantedThisRun.Add(self);
                Log.Info($"Aspect '{wantedName}' donne au joueur.");
            }
        }

        // Retrouve le NetworkUser d'un master de joueur.
        private static NetworkUser GetNetworkUser(CharacterMaster master)
        {
            if (master == null)
                return null;
            foreach (NetworkUser nu in NetworkUser.readOnlyInstancesList)
            {
                if (nu != null && nu.master == master)
                    return nu;
            }
            return null;
        }

        // Choix d'aspect pour un joueur donne : son choix reseau, sinon (pour
        // l'hote / le joueur local) la config locale en secours.
        private static string GetChoiceForMaster(CharacterMaster master)
        {
            NetworkUser nu = GetNetworkUser(master);
            if (nu != null && aspectByUser.TryGetValue(nu.netId, out string v) && !string.IsNullOrEmpty(v))
                return v;

            if (nu != null && nu.isLocalPlayer)
                return chosenAspect.Value;

            return "";
        }

        // Aspects caches / non utilises du jeu a NE PAS proposer.
        private static readonly HashSet<string> excludedAspects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EliteSecretSpeedEquipment", // "Au-dela des limites" : aspect cache (oreilles de lapin)
        };

        // Enumere tous les equipements d'aspect d'elite (DLC inclus), sauf ceux exclus.
        // Methode robuste : un equipement est un aspect si son buff passif est un buff d'elite.
        private static IEnumerable<EquipmentDef> GetAllAspects()
        {
            for (int i = 0; i < EquipmentCatalog.equipmentCount; i++)
            {
                EquipmentDef def = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)i);
                if (def != null && def.passiveBuffDef != null && def.passiveBuffDef.isElite
                    && !excludedAspects.Contains(def.name))
                    yield return def;
            }
        }

        // Trouve l'EquipmentIndex correspondant a un nom (interne ou affiche).
        private static EquipmentIndex FindAspectByName(string wanted)
        {
            wanted = wanted?.Trim();
            if (string.IsNullOrEmpty(wanted))
                return EquipmentIndex.None;

            foreach (EquipmentDef eq in GetAllAspects())
            {
                string internalName = eq.name ?? "";
                string display = Language.GetString(eq.nameToken) ?? "";

                if (internalName.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
                    display.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return eq.equipmentIndex;
                }
            }

            Log.Warning($"Aspect '{wanted}' introuvable. Verifie l'orthographe (liste dans la console).");
            return EquipmentIndex.None;
        }

        // ============================================================
        //  PHASE 3 : reseau (envoyer le choix de chaque joueur au serveur).
        // ============================================================

        // A l'entree en selection de perso, le client envoie son choix au serveur
        // (couvre le cas ou le joueur ne clique pas et garde son choix de config).
        private void OnCharacterSelectAwake(
            On.RoR2.UI.CharacterSelectController.orig_Awake orig,
            RoR2.UI.CharacterSelectController self)
        {
            orig(self);
            StartCoroutine(SendMyChoiceWhenReady());
        }

        // Attend que le NetworkUser local existe puis envoie le choix au serveur.
        private IEnumerator SendMyChoiceWhenReady()
        {
            float t = 0f;
            NetworkUser nu = null;
            while (t < 5f)
            {
                nu = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
                if (nu != null)
                    break;
                t += Time.deltaTime;
                yield return null;
            }
            SendMyChoice();
        }

        // Envoie le choix d'aspect du joueur local au serveur.
        private static void SendMyChoice()
        {
            try
            {
                NetworkUser nu = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
                if (nu == null)
                    return;
                new SetAspectMessage(nu.netId, chosenAspect.Value ?? "").Send(NetworkDestination.Server);
            }
            catch (Exception e)
            {
                Log.Warning("Envoi du choix d'aspect echoue : " + e);
            }
        }

        // Message reseau : un joueur transmet (son netId + son aspect) au serveur.
        public class SetAspectMessage : INetMessage
        {
            private NetworkInstanceId userNetId;
            private string aspectName;

            public SetAspectMessage() { }

            public SetAspectMessage(NetworkInstanceId id, string name)
            {
                userNetId = id;
                aspectName = name ?? "";
            }

            public void Serialize(NetworkWriter writer)
            {
                writer.Write(userNetId);
                writer.Write(aspectName ?? "");
            }

            public void Deserialize(NetworkReader reader)
            {
                userNetId = reader.ReadNetworkId();
                aspectName = reader.ReadString();
            }

            public void OnReceived()
            {
                // Seul le serveur stocke les choix.
                if (!NetworkServer.active)
                    return;
                aspectByUser[userNetId] = aspectName ?? "";
            }
        }

        // ============================================================
        //  PHASE 2 : ligne "Aspect" dans le panneau Arsenal.
        //  On clone une ligne existante (Row) et on remplace ses boutons
        //  par une icone cliquable par aspect (+ un bouton "aucun").
        // ============================================================

        // Boutons d'aspect crees, avec la cle (nom interne) associee.
        private static readonly List<KeyValuePair<GameObject, string>> aspectButtons =
            new List<KeyValuePair<GameObject, string>>();

        // Modele de ligne sain, capture UNE fois et reutilise (voir AddAspectRow).
        private static GameObject cachedRowTemplate;

        private void OnLoadoutRebuild(
            On.RoR2.UI.LoadoutPanelController.orig_Rebuild orig,
            RoR2.UI.LoadoutPanelController self)
        {
            orig(self);

            // On entoure d'un try/catch : si l'UI change dans une future maj,
            // ca ne cassera ni le panneau ni l'attribution.
            try
            {
                AddAspectRow(self);
            }
            catch (Exception e)
            {
                Log.Warning("Ajout de la ligne Aspect echoue : " + e);
            }
        }

        private void AddAspectRow(RoR2.UI.LoadoutPanelController self)
        {
            if (self == null)
                return;

            // Rebuild recree tout : on enleve d'abord une eventuelle ancienne ligne.
            Transform old = self.transform.Find("AspectRow");
            while (old != null)
            {
                DestroyImmediate(old.gameObject);
                old = self.transform.Find("AspectRow");
            }

            // 1) Capturer UNE FOIS un modele de ligne sain et le garder en cache.
            //    A la 2e ouverture, les lignes du jeu sont en cours de destruction :
            //    en cloner une donnait une ligne cassee (hauteur 0). On clone donc
            //    toujours depuis cette copie en cache, jamais depuis les lignes vivantes.
            if (cachedRowTemplate == null)
            {
                Transform live = null;
                foreach (Transform child in self.transform)
                {
                    if (child.name.StartsWith("Row") && child.gameObject.activeInHierarchy)
                    {
                        live = child;
                        break;
                    }
                }
                if (live == null)
                    return;

                cachedRowTemplate = Instantiate(live.gameObject);
                cachedRowTemplate.name = "AspectRowTemplate";
                cachedRowTemplate.SetActive(false);
                DontDestroyOnLoad(cachedRowTemplate);
            }

            // 2) Cloner depuis le cache et placer en bas du panneau.
            GameObject newRow = Instantiate(cachedRowTemplate, self.transform);
            newRow.SetActive(true);
            newRow.name = "AspectRow";
            // En bas du panneau, sous "Apparence".
            newRow.transform.SetAsLastSibling();

            // IMPORTANT : forcer une vraie hauteur. La ligne clonee se retrouvait a
            // hauteur 0 -> ses icones debordaient hors du panneau (invisibles en bas).
            // On force a la fois le LayoutElement ET le RectTransform pour couvrir
            // les deux modes possibles du VerticalLayoutGroup parent.
            const float rowHeight = 80f;
            var rowLayout = newRow.GetComponent<LayoutElement>();
            if (rowLayout == null)
                rowLayout = newRow.AddComponent<LayoutElement>();
            rowLayout.minHeight = rowHeight;
            rowLayout.preferredHeight = rowHeight;
            rowLayout.flexibleHeight = 0f;
            rowLayout.ignoreLayout = false;

            var rowRect = newRow.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(rowRect.sizeDelta.x, rowHeight);

            // IMPORTANT : la ligne clonee a un Canvas avec un ordre de rendu force
            // (overrideSorting) qui la fait passer DERRIERE le panneau de maniere
            // aleatoire -> elle "disparait". On desactive ce forcage pour qu'elle
            // suive l'ordre normal de la hierarchie (donc toujours visible).
            foreach (var cv in newRow.GetComponentsInChildren<Canvas>(true))
                cv.overrideSorting = false;
            foreach (var refresh in newRow.GetComponentsInChildren<RefreshCanvasDrawOrder>(true))
                refresh.enabled = false;

            // 3) Changer le titre de la ligne en "ASPECT".
            Transform slotLabel = newRow.transform.Find("SlotLabel");
            if (slotLabel != null)
            {
                var lang = slotLabel.GetComponent<LanguageTextMeshController>();
                if (lang != null)
                    lang.enabled = false; // empeche le jeu de reecrire le texte
                var tmp = slotLabel.GetComponent<HGTextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = "ASPECT";
                    // Couleur fixe (sinon on herite de la couleur du perso mis en cache).
                    tmp.color = Color.white;
                }
            }

            // 4) Reconstruire les boutons dans le ButtonContainer.
            Transform container = newRow.transform.Find("ButtonContainer");
            if (container == null)
                return;

            Transform spacer = container.Find("Spacer");

            // Boutons existants (skills) : on en garde un comme modele, on detruit le reste apres.
            var existing = new List<Transform>();
            foreach (Transform c in container)
            {
                if (c.name.Contains("LoadoutButton"))
                    existing.Add(c);
            }
            if (existing.Count == 0)
                return;
            Transform buttonTemplate = existing[0];

            aspectButtons.Clear();

            // Bouton "aucun aspect" (cle = chaine vide), puis un par aspect.
            CreateAspectButton(container, buttonTemplate, null);
            foreach (EquipmentDef eq in GetAllAspects())
                CreateAspectButton(container, buttonTemplate, eq);

            // Detruire les boutons de skill d'origine (modele inclus).
            foreach (Transform b in existing)
                Destroy(b.gameObject);

            // Garder l'espaceur en derniere position.
            if (spacer != null)
                spacer.SetAsLastSibling();

            ApplyHighlight();

            // Forcer un recalcul immediat de la mise en page pour que la hauteur
            // soit prise en compte tout de suite (sinon la ligne peut rester a 0).
            LayoutRebuilder.ForceRebuildLayoutImmediate(self.transform as RectTransform);
        }

        private void CreateAspectButton(Transform container, Transform template, EquipmentDef aspect)
        {
            GameObject go = Instantiate(template.gameObject, container);
            go.name = "AspectBtn";
            go.SetActive(true);

            // Icones plus petites pour que les 13 tiennent mieux sur la ligne.
            var le = go.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredWidth = 48;
                le.preferredHeight = 48;
                le.minWidth = 48;
                le.minHeight = 48;
            }

            string key = aspect == null ? "" : aspect.name;

            // Icone du bouton.
            var img = go.GetComponent<Image>();
            if (img != null)
            {
                if (aspect != null)
                {
                    img.sprite = aspect.pickupIconSprite;
                    img.color = Color.white;
                }
                else
                {
                    // Bouton "aucun aspect" : pas d'icone, case sombre.
                    img.sprite = null;
                    img.color = new Color(0.12f, 0.12f, 0.12f, 1f);
                }
            }

            // Infobulle : le bouton clone garde le composant fige de la competence.
            // On ecrase TOUS les champs texte candidats par reflexion (token ET texte
            // force) pour remplacer ceux que l'infobulle affiche reellement.
            var tip = go.GetComponent<TooltipProvider>();
            if (tip != null)
            {
                string titleTok, bodyTok, titleText, bodyText;
                if (aspect != null)
                {
                    titleTok = aspect.nameToken;
                    bodyTok = aspect.pickupToken;
                    titleText = Language.GetString(titleTok);
                    // On prend le premier token qui donne un vrai texte (pickup, puis desc).
                    bodyText = ResolveFirst(aspect.pickupToken, aspect.descriptionToken);
                    if (string.IsNullOrEmpty(bodyText))
                        bodyText = "Aspect d'elite.";
                }
                else
                {
                    titleTok = bodyTok = "";
                    titleText = "Aucun aspect";
                    bodyText = "Commencer la partie sans aspect.";
                }

                SetStringField(tip, "titleToken", titleTok);
                SetStringField(tip, "bodyToken", bodyTok);
                SetStringField(tip, "overrideTitleText", titleText);
                SetStringField(tip, "overrideBodyText", bodyText);
            }

            // Clic : on ecrit le choix dans la config (lue par la Phase 1).
            var btn = go.GetComponent<HGButton>();
            if (btn != null)
            {
                // On efface tous les anciens listeners (y compris ceux du prefab).
                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener(() =>
                {
                    chosenAspect.Value = key;
                    ApplyHighlight();
                    // Multi : transmettre le choix au serveur.
                    SendMyChoice();
                });
            }

            aspectButtons.Add(new KeyValuePair<GameObject, string>(go, key));
        }

        // Renvoie le premier token qui se traduit par un vrai texte (pas vide, pas "???").
        private static string ResolveFirst(params string[] tokens)
        {
            foreach (var t in tokens)
            {
                if (string.IsNullOrEmpty(t))
                    continue;
                string s = Language.GetString(t);
                if (!string.IsNullOrEmpty(s) && s != "???" && s != t)
                    return s;
            }
            return "";
        }

        // Affecte une valeur a un champ string par son nom (s'il existe).
        private static void SetStringField(object obj, string fieldName, string value)
        {
            var f = obj.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(string))
                f.SetValue(obj, value);
        }

        // Met en evidence le bouton selectionne (plein) et attenue les autres.
        private void ApplyHighlight()
        {
            string sel = (chosenAspect.Value ?? "").Trim();

            foreach (var kv in aspectButtons)
            {
                if (kv.Key == null)
                    continue;

                bool isSelected =
                    string.Equals(kv.Value, sel, StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrEmpty(sel) && string.IsNullOrEmpty(kv.Value));

                var img = kv.Key.GetComponent<Image>();
                if (img != null)
                {
                    Color c = img.color;
                    c.a = isSelected ? 1f : 0.4f;
                    img.color = c;
                }

                kv.Key.transform.localScale = isSelected ? Vector3.one * 1.1f : Vector3.one;
            }
        }

        // Logge la liste des aspects disponibles, a recopier dans la config.
        private static void LogAvailableAspects()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Aspects disponibles (recopie un de ces noms dans la config) ===");
            foreach (EquipmentDef eq in GetAllAspects())
            {
                string display = Language.GetString(eq.nameToken);
                sb.AppendLine($"  - {eq.name}    ({display})");
            }
            Log.Info(sb.ToString());
        }
    }
}
