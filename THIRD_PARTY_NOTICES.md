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

The pet and room geometry are authored in this repository. The application icon is rendered from repository-owned drawing code. No downloaded image, character, model, texture, or sound asset is bundled. Optional feedback uses Windows system sounds. Fonts are selected from the user's Windows installation and are not redistributed.

Full applicable license texts are included in the packaged `licenses` directory; notices do not establish a license for Forme's own source code.
