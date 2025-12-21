# Documentation API Gauniv (rapide)

Ce fichier décrit rapidement les endpoints exposés par le serveur Web `Gauniv.WebServer` et explique comment les tester à l'aide de la collection Postman fournie.

Base URL (par défaut en développement) :
- http://localhost:5231

Remarque : en développement un utilisateur de test est seedé par `SetupService` :
- Email : `test@test.com`
- Mot de passe : `password`

## Comptes de test (ToDB_User.json)

Un fichier `ToDB_User.json` est fourni à la racine du projet pour seed des comptes de développement :

- **client@test.local**
  - Email / UserName : `client@test.local`
  - Password : `Pa$$w0rdClient!`
  - Roles : `User`
  - Usage : utilisateur standard pour tester l'interface et les endpoints publics/protégés (non‑admin).

- **admin@test.local**
  - Email / UserName : `admin@test.local`
  - Password : `Pa$$w0rdAdmin!`
  - Roles : `Admin`
  - Usage : compte administrateur pour tester les pages/actions restreintes (onglet "Gestion" visible).

> Remarque de sécurité : ces comptes et mots de passe sont fournis pour le développement local uniquement ; ne jamais conserver de mots de passe en clair en production.

### Seed rapide depuis `ToDB_User.json`

Copier le fichier `ToDB_User.json` dans la racine du projet (déjà présent) puis ajouter un petit seed au démarrage (ex. dans `Program.cs`) qui lit le JSON, crée les rôles et les utilisateurs si nécessaire. Voir le snippet de seed fourni dans le README général du projet.

Après seed, connectez‑vous avec `admin@test.local` pour vérifier l'apparition de l'onglet **Gestion** et l'accès aux pages administrateur.

Collection Postman
- Importer la collection : `Gauniv_WebServer_API - Tests.postman_collection.json`.
- La collection contient des requêtes prêtes à l'emploi pour : login, lister catégories, lister jeux (publics) et lister "mes jeux" (avec JWT).

Authentification
- Endpoint : `POST /api/1.0.0/auth/login`
  - Body JSON : `{ "email": "...", "password": "..." }`
  - Réponse : `{ "Token": "<jwt>", "ExpiresAtUtc": "..." }`
- Pour appeler un endpoint protégé, ajouter l'entête :
  - `Authorization: Bearer <token>`
- L'API accepte aussi la session/cookie : l'accès à la ressource "mes jeux" est possible si l'utilisateur est connecté via cookie OU s'il fournit un JWT valide.

Endpoints principaux

1) Lister les catégories (public)
- GET `/api/1.0.0/Game/Categories`
- Réponse : liste de catégories (objets contenant `Libelle`).

2) Lister les jeux (public ou "mes jeux")
- GET `/api/1.0.0/Game/Games`
- Query params supportés :
  - `offset` (int, optionnel, défaut 0)
  - `limit` (int, optionnel, défaut 20)
  - `category` (int) ou `category[]` (plusieurs) — filtre par id de catégorie
  - `owned` (bool) — si `true` renvoie uniquement les jeux possédés par l'utilisateur (requiert session OU JWT)
- Exemples :
  - `/api/1.0.0/Game/Games?offset=10&limit=15`
  - `/api/1.0.0/Game/Games?category=3`
  - `/api/1.0.0/Game/Games?category[]=3&category[]=4`
  - `/api/1.0.0/Game/Games?offset=10&limit=15&category[]=3&owned=true`
- Réponse : liste de `GameDtoLight` (propriétés principales : `Id`, `Name`, `Description`, `Price`, `Categories` (liste de noms)).

Notes techniques et sécurité
- Le JWT est configuré via la section `Jwt` dans `appsettings.json` (clé de développement présente). En production, utiliser une clé secrète stockée en variable d'environnement ou dans un gestionnaire de secrets.
- Pour `owned=true` l'API accepte :
  - un utilisateur authentifié via middleware (session/cookie), OU
  - un client fournissant un JWT valide dans l'entête Authorization. Si aucune méthode d'authentification ne permet d'identifier l'utilisateur, la réponse est `401 Unauthorized`.
- Pagination et filtres sont appliqués côté base de données.

Exemples rapides (PowerShell) :

Obtenir un token (login) :
```powershell
$body = @{ Email = "test@test.com"; Password = "password" } | ConvertTo-Json
$resp = Invoke-RestMethod -Uri "http://localhost:5231/api/1.0.0/auth/login" -Method Post -Body $body -ContentType "application/json"
$token = $resp.Token
```

Appeler la liste "mes jeux" avec le JWT :
```powershell
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Uri "http://localhost:5231/api/1.0.0/Game/Games?owned=true&limit=20" -Method Get -Headers $headers
```

Dépannage rapide
- Si le port est occupé au démarrage (`address already in use`) : arrêter le processus qui utilise le port ou changer le port d'écoute avant de relancer.
- Si la clé JWT n'est pas configurée, les endpoints protégés basés sur token renverront une erreur serveur. Vérifier `appsettings.json` ou vos variables d'environnement.

Support & suite
- Utiliser la collection Postman `Gauniv_WebServer_API - Tests.postman_collection.json` pour des tests rapides et pour automatiser des scénarios.