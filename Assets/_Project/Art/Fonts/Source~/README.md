# Gravewire + Cyrillic

`Gravewire-Regular.original.ttf` is LESED Gravewire Free (see `LICENSE.txt`: use in the game only, don't redistribute the font files).
`add_cyrillic.py` draws the Russian alphabet as pixel bitmaps and writes `../Gravewire-Regular.ttf`:

```
python add_cyrillic.py Gravewire-Regular.original.ttf ../Gravewire-Regular.ttf
```

Needs `pip install fonttools`. After rebuilding, regenerate the atlas of `Gravewire SDF` (Inspector → Update Atlas Texture,
or Font Asset Creator: sampling 110, padding 10, SDFAA, 2048x1024, Static). 110 pt = 10 texels per font pixel.
