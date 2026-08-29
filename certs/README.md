# Certificats TLS (mode `custom`)

Utilisé quand `RESULTS_TLS_MODE=custom` dans `.env`.

Place ici :

| Fichier | Contenu |
|---|---|
| `fullchain.pem` | Certificat serveur + chaîne CA (PEM) |
| `privkey.pem` | Clé privée (PEM) |

Ces fichiers ne sont **pas** versionnés (voir `.gitignore`).

Sur les postes simulateur (Windows), installe la CA racine de ta PKI dans « Autorités de certification racines de confiance » pour éviter d’ignorer les erreurs TLS.
