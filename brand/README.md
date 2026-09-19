# Brand assets

Drop replacement artwork here and copy it over the files the app actually loads.
Everything the launcher renders lives in `src/Launcher/Assets/`.

| What | File the app loads | Notes |
| --- | --- | --- |
| Wordmark (hero) | `src/Launcher/Assets/wordmark.png` | Transparent PNG, ~760 px wide. Currently a stand-in built from the Free Realms wordmark recoloured gold — **replace with the real gold "FreeRealms LEGACY" logo**. |
| Tree icon | `src/Launcher/Assets/logo.png` | 256x256 transparent PNG. Currently the gold-outlined Legacy tree. |
| App icon | `src/Launcher/App.ico` | Multi-size .ico generated from the tree. |
| Hero background | `src/Launcher/Assets/background.png` | 1200x672. Currently official Free Realms lake art from the website assets. |

The tree and the .ico were generated from
`D:\OSFR-Live\Website\public\download\legacystudio\icon.png`.

To regenerate the .ico after replacing the tree:

```bash
python -c "from PIL import Image; im=Image.open('src/Launcher/Assets/logo.png'); im.save('src/Launcher/App.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])"
```
