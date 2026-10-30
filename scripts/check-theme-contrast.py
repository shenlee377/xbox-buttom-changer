"""Check foreground/background contrast in both shipped desktop palettes."""
from pathlib import Path
from xml.etree import ElementTree as ET

def luminance(color):
    values = [int(color[i:i+2], 16) / 255 for i in (1, 3, 5)]
    linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in values]
    return sum(v * weight for v, weight in zip(linear, (.2126, .7152, .0722)))

for theme in ('Light', 'Dark'):
    path = Path(__file__).resolve().parents[1] / f'src/GuideDeck.Desktop/Themes/{theme}.xaml'
    values = {node.attrib['{http://schemas.microsoft.com/winfx/2006/xaml}Key']: node.attrib['Color'] for node in ET.parse(path).getroot()}
    for foreground, background, minimum in (
        ('TextBrush', 'SurfaceBrush', 4.5), ('TextBrush', 'InputBrush', 4.5),
        ('MutedTextBrush', 'SurfaceBrush', 4.5), ('MutedTextBrush', 'AppBackgroundBrush', 4.5),
        ('AccentTextBrush', 'AccentBrush', 4.5), ('AccentSoftTextBrush', 'AccentSoftBrush', 4.5),
        ('NoticeTextBrush', 'NoticeBrush', 4.5), ('ControlBorderBrush', 'InputBrush', 3.0),
    ):
        low, high = sorted((luminance(values[foreground]), luminance(values[background])))
        contrast = (high + .05) / (low + .05)
        assert contrast >= minimum, (theme, foreground, background, contrast)
        print(f'PASS {theme}: {foreground}/{background} = {contrast:.2f}:1')
