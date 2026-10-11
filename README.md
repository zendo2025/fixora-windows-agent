# Fixora Windows Agent

Agent Windows local du service de diagnostic PC Fixora.

## Fonctionnement

- détecte le processeur, la mémoire et les cartes graphiques avec WMI ;
- lit les capteurs disponibles avec LibreHardwareMonitor ;
- utilise `nvidia-smi` lorsqu'il est disponible ;
- écoute uniquement sur `http://127.0.0.1:38471` ;
- protège chaque session avec un jeton temporaire aléatoire.

## Compilation automatique

Chaque envoi sur la branche `main` déclenche GitHub Actions. L'exécutable compilé
est disponible dans l'artefact `Fixora-Agent-Windows` du workflow.

## Utilisation

1. Téléchargez et décompressez l'artefact.
2. Lancez `FixoraAgent.exe` en tant qu'administrateur.
3. Gardez la fenêtre ouverte pendant le diagnostic.

Pour ouvrir une autre adresse de diagnostic :

```text
FixoraAgent.exe "https://votre-domaine.example/diagnostic-pc"
```

## Sécurité et confidentialité

L'agent ne doit contenir aucun mot de passe, jeton GitHub, clé privée ni secret
du serveur. Vérifiez le code avant chaque publication.

## Licences

Le code de Fixora Windows Agent est distribué sous licence MIT.
LibreHardwareMonitor et ses dépendances conservent leurs licences respectives.
