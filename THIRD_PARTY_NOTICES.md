# Third-party notices

Forme bundles the following components in its Windows self-contained distribution. Exact versions and content hashes are recorded in the project `packages.lock.json` files.

- .NET / WPF runtime: Microsoft, MIT and bundled third-party licenses. [dotnet runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [runtime third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT), [WPF license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT).
- Microsoft.Data.Sqlite 10.0.9: Microsoft, MIT. [license](https://github.com/dotnet/efcore/blob/main/LICENSE.txt).
- HtmlAgilityPack 1.13.1: ZZZ Projects, MIT. [license](https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE), included as `licenses/HtmlAgilityPack.txt`.
- ModelContextProtocol.Core 2.2.0: Model Context Protocol contributors. Package declares Apache-2.0; upstream licensing-transition text and retained MIT notice are included in `licenses/ModelContextProtocol.txt`. [license at package source revision](https://github.com/modelcontextprotocol/csharp-sdk/blob/6fa3825973949a9c4f0cd8af344e15a8db09dc35/LICENSE).
- Microsoft.Extensions.AI.Abstractions 10.8.3, Microsoft.Extensions.Logging.Abstractions and Microsoft.Extensions.DependencyInjection.Abstractions 10.0.10: Microsoft / .NET Foundation, MIT. Included as `licenses/Microsoft.Extensions.txt` and `licenses/dotnet-runtime.txt`. These libraries provide protocol/tool and logging interfaces; no external AI service is enabled by installing them.
- Windows SDK .NET projection 10.0.19041.57: Microsoft. [Windows SDK license](https://aka.ms/WinSDKLicenseURL), included as `licenses/Windows.SDK.rtf`. Used for local Windows PDF rendering; the PDF renderer itself is part of Windows and is not redistributed.
- WinRT.Runtime (bundled by the Windows SDK projection): Microsoft C#/WinRT, MIT. [license](https://github.com/microsoft/CsWinRT/blob/master/LICENSE), included as `licenses/WinRT.Runtime.txt`.
- SQLitePCLRaw 3.0.5: Eric Sink / SourceGear, Apache-2.0. [license](https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE.TXT).
- SQLite native 3.53.4: distributed by SourceGear. Refer to the included `licenses/SQLite.txt` for the package's terms and SQLite public-domain notice.

The default sprout, cat, fox, penguin, room geometry, and application icon are authored in this repository. Six optional companion models (Ranger, Cleric, Warrior, Adventurer, Casual, Formal) are from Quaternius's RPG Character Pack and Ultimate Modular Women Pack and dedicated to the public domain under CC0 1.0; source links, original asset names, and SHA-256 hashes are recorded in `licenses/Quaternius-Models.md`. Three anime-inspired female avatars (Rose, Olivia, Kate) are from Polygonal Mind's 100 Avatars Round 1 CC0 collection; source URLs, original names, and hashes are recorded in `licenses/Polygonal-Mind-100-Avatars.md`. The pack license files and CC0 deed are included under `licenses/`. Optional feedback uses Windows system sounds. Fonts are selected from the user's Windows installation and are not redistributed.

Full applicable license texts are included in the packaged `licenses` directory; notices do not establish a license for Forme's own source code.
# Optional local Genshin Impact avatar (not redistributed)

The optional Hu Tao VRM is downloaded/imported separately by the user and is not included in Forme's source or release packages. Character rights belong to their original rights holders; the file records Impmagik as its VRM conversion author. The resource's metadata permits use by others but prohibits redistribution. Source, checksum, recorded conditions and limitations: `licenses/Genshin-Local-Models.md`. This is an independent desktop companion and is not affiliated with or endorsed by the game's rights holders.
