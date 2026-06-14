# StartWithAspect

Un mod **Risk of Rain 2** qui te laisse choisir un **aspect d'élite** avec lequel commencer la partie, directement depuis la fenêtre **Arsenal** de l'écran de sélection de personnage.

![Aperçu](Thunderstore/icon.png)

## Fonctionnalités

- Une ligne **ASPECT** ajoutée dans l'Arsenal : clique une icône pour choisir l'aspect de départ (ou « aucun »).
- Tous les aspects sont détectés automatiquement, **DLC inclus** (Blazing, Overloading, Glacial, Malachite, Celestine, Perfected, Void, etc.).
- Au lancement de la partie, le personnage démarre avec l'aspect équipé (il devient cet élite : aura + effet).
- **Multijoueur** : chaque joueur choisit et démarre avec son propre aspect (synchronisé via R2API Networking).
- Réglage de config `Aspect de depart` comme sélecteur de secours.

## Dépendances

- BepInEx (BepInExPack)
- R2API Networking
- HookGenPatcher

## Compiler

Ouvre `RoR2Mods.sln` dans Visual Studio (workload « .NET desktop »), laisse NuGet restaurer les paquets, puis génère la solution (`Ctrl+Maj+B`). Le `.dll` est produit dans `ExamplePlugin/bin/<Config>/netstandard2.1/`.

Copie ce `.dll` dans `BepInEx/plugins/StartWithAspect/` de ton profil (r2modman recommandé) pour le tester.

## Publier

Le dossier `Thunderstore/` contient le `manifest.json`, l'`icon.png` (256×256), le `README.md` et le `CHANGELOG.md` à empaqueter en `.zip` pour un envoi sur [Thunderstore](https://thunderstore.io/c/riskofrain2/).

## Licence

MIT — voir `LICENSE`.
