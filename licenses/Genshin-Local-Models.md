# Optional local Genshin Impact avatar

Forme does **not** redistribute this model. No Genshin VRM is embedded, copied into the release payload, committed to the repository, or included in data exports. The optional model is kept in the active Forme data directory under `Models/genshin-hu-tao.vrm`.

## Tested resource

- Character: Hu Tao / 胡桃, Genshin Impact. Character rights remain with their original rights holders; Forme is not an official or affiliated product.
- VRM conversion author recorded in the file: **Impmagik**.
- Resource page: https://github.com/AashooSharma/VRM-models/blob/main/Genshin/Hu%20Tao.vrm
- Repository blob: `db19fa27c8072a61050c2005d9dc3324eaf1444e`.
- File length: `7721368` bytes.
- SHA-256: `E6E86FB91C28417864ED30305A434541FB9CF214DBCB6F2555AF8795A5E374C7`.
- Checked: 2026-10-21. The repository is a third-party resource collection, not an official publisher channel. It has no repository-wide license granting rights over these models.

The tested file's VRM 0 metadata states `allowedUserName=Everyone`, `licenseName=Other`, and links to the following model conditions:

https://hub.vroid.com/license?allowed_to_use_user=everyone&characterization_allowed_user=everyone&corporate_commercial_use=allow&credit=unnecessary&modification=allow&personal_commercial_use=profit&redistribution=disallow&sexual_expression=disallow&version=1&violent_expression=allow

In particular, **redistribution is disallowed**. These are the file's recorded conditions, not a claim that the converter or repository owner can grant rights belonging to the game's rights holders. Do not interpret public availability, the repository's description, or any commercial-use metadata as an unrestricted license. Obtain any additional authorization required for the intended use.

Other checked files (Lumine, Barbara, Furina and Mavuika) carry `OnlyAuthor` and/or restrictive redistribution metadata. They are not supported or installed by this change. One resource advertised as CC0 also contains contradictory restrictive text; it is not included.

## Local use and adaptation

The Settings page has an explicit **导入并使用胡桃 VRM** button for the exact tested resource. It checks the length and SHA-256 before copying. A different file is rejected without replacing the installed copy. The manual `scripts/ImportGenshinHuTao.ps1` helper downloads this resource only when explicitly run, verifies the same digest, and writes only the active model file. It does not send chats, API keys, local file contents, or device information to the model host.

Importing and animating are local operations. Existing waiting, dancing, room navigation, toys, and desktop modes use the standard `PetPose` interface and VRM semantic humanoid bones. The avatar is normalized and rotated to match Forme's +Z forward convention. Source files are not rewritten. The application does not reproduce original game animations, skills, MToon shaders, or hair/clothing physics, and does not currently map this avatar's facial morph targets.

Only referenced vertices are retained per material for skinning: **18176 vertices, 20632 triangles, 25 primitives** for this file. This is a geometry count, not a CPU/GPU performance guarantee. Hidden and reduced-motion views retain the existing pause rules. After a missing or invalid local file, startup falls back to the sprout; settings and AI switching report that the model needs importing rather than claiming success.

After reinstalling on the same machine with the same data directory, the optional model remains available. When moving to a different machine, obtain the model separately and import there according to its conditions; Forme's data export does not contain it.
