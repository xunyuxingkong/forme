# Quaternius companion models

The following complete, textured humanoid models are bundled from Quaternius's official packs. The pack pages label the packs CC0 and describe rigged, animated, textured characters. Their `glTF` files embed buffers and textures. Forme packages GLB conversions of the original files for its model loader; original source-file and bundled-file SHA-256 hashes are listed below.

| Forme display name | Original file | Official pack | Google Drive asset ID | Source SHA-256 | Bundled GLB SHA-256 |
| --- | --- | --- | --- | --- | --- |
| 巡林游侠 | `Ranger.gltf` | [RPG Character Pack](https://quaternius.com/packs/rpgcharacters.html) | `1e3eAO6oHYVyDNdhoNogNii5CtLQB7pLc` | `2732741D5E21C20760D7F3DD9D71812D4B796F466BE1A6CBA212F45483DD56ED` | `ACC4EF52F62CABDE1E924A50E77F58338732D807D760BD93FE5AF1CD0C7A5AA7` |
| 星辉牧师 | `Cleric.gltf` | [RPG Character Pack](https://quaternius.com/packs/rpgcharacters.html) | `1LlmtVPWTRVbB5rXemboYstI5lor7I8vO` | `6497456A3FA1C151D7A6AB4F7B4C343EE416C23D9454505B950C93EA558E4601` | `63F1519C9752B0999FC35287C14A800B9AC04B2A807601B43CEC14D759B0C7AC` |
| 守誓战士 | `Warrior.gltf` | [RPG Character Pack](https://quaternius.com/packs/rpgcharacters.html) | `1oALqyqy7yVQnLh-Vs19kKg3eZgJqIgoc` | `2BAB905CE12D279E1D099C2A8DE0B1EF31C9F5E8D1959B41D24470B4F4181ADD` | `32512D24A1A6DB6194E9D4D6BD63C7D655CAB5757F0A73386A70C5CB7EE351F5` |

The following three additional female-presenting characters are from Quaternius's Ultimate Modular Women Pack. Each original glTF has one rigged skin and 24 animation clips.

| Forme display name | Original file | Official pack | Google Drive asset ID | Source SHA-256 | Bundled GLB SHA-256 |
| --- | --- | --- | --- | --- | --- |
| 星旅冒险家 | `Adventurer.gltf` | [Ultimate Modular Women Pack](https://quaternius.com/packs/ultimatemodularwomen.html) | `1uxAFnDp73NO1c16LvHHjAYh1-deMNk5I` | `65094211E53B49F6A834C617CC056834686FE794A0B515A6B97FAA3E1130CC95` | `CDC87DA70E1286E6B31415AEFB452AE1D3E002B83407AAD2D4528F1BF4E61AD2` |
| 轻装少女 | `Casual.gltf` | [Ultimate Modular Women Pack](https://quaternius.com/packs/ultimatemodularwomen.html) | `18b3WwlrwrFYWAM7BcnjWeIxKJyxAQiGh` | `B0FE6E92219CD71808844A20A1A8B960FD1CF640A6546DC6362B5ADD6604E87C` | `DFE2538E2851757CE554E2638E519910ED7FEC4CAE360D68A536B4293C662E31` |
| 典雅女士 | `Formal.gltf` | [Ultimate Modular Women Pack](https://quaternius.com/packs/ultimatemodularwomen.html) | `1iayBzVv_zLjuPtaNPouw_auwKlQLLmes` | `FDFCF454C4DE037D31973EB28D8591BD7763035D49F46B380E63B1C832DC7DDF` | `914A401753CF768D0840EF944418AB105552BB51D79A48C8EE3BD9C26FD435FF` |

The RPG source glTF files contain 14, 11, and 13 animation clips respectively. Forme uses each model's original `Idle`, `Walk`, and `Run` clips, plus `PickUp` for reach interactions. The Women Pack models use their original `Idle`, `Walk`, and `Run` clips; reach interactions use `Interact` and arm gestures can use `Wave`. The shared pet pose signals are also applied to head and arm joints. Stable internal preference IDs remain unchanged so existing user settings continue to resolve.

The original pack license files are included as `Quaternius-RPG-License.txt` and `Quaternius-Women-License.txt`; both state CC0 1.0. The CC0 deed is also included as `CC0-1.0.txt`. Source pages: [Quaternius RPG Character Pack](https://quaternius.com/packs/rpgcharacters.html) and [Ultimate Modular Women Pack](https://quaternius.com/packs/ultimatemodularwomen.html). Download folders: [RPG Character Pack](https://drive.google.com/drive/folders/1MIRQXLfTd21HMI5rwOb6Xy0rv0xv1m8b?usp=sharing) and [Women Pack individual characters](https://drive.google.com/drive/folders/1_FIjjIVE0SkQIrwgWJ3Q8NQku25ll5a2). The assets are embedded in the application and are not downloaded at runtime.
