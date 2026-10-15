# Home Assistant resources

Resources for showing existing Home Assistant dashboards on an izBoard e-paper display.

## Themes

The display shows only black, red and white. izBoard maps every pixel of a captured dashboard to
the nearest of these colors (optionally with dithering), so regular themes lose greys, tints and
shadows or turn them into noise. [`themes/epaper.yaml`](themes/epaper.yaml) defines the
`epaper-red-black-white` theme, which uses only the three display colors: black text and borders,
red for active states, accents and warnings, and white backgrounds.

### Install

1. Copy `themes/epaper.yaml` to the `themes` folder in your Home Assistant configuration directory.
2. Load the folder from `configuration.yaml`, if it is not loaded already:

   ```yaml
   frontend:
     themes: !include_dir_merge_named themes
   ```

3. Restart Home Assistant, or call the `frontend.reload_themes` action.

### Use

Apply the theme to the view that izBoard captures, so the result does not depend on the profile of
the Home Assistant user the renderer signs in with. In the dashboard's raw configuration editor:

```yaml
views:
  - title: E-paper
    path: e-paper
    theme: epaper-red-black-white
    cards: []
```

Alternatively, select the theme in the profile of the user izBoard signs in with.

### Card notes

Cards do not all read the same theme variables, so the theme also covers the common cards and the
components they share. What to expect on the display:

| Card | Result |
| --- | --- |
| Tile, button, glance, entity | Active states red, inactive black, unavailable red. The tile icon background is fixed at 20% opacity and disappears. |
| Entities | Toggles are outlined black when off and filled red when on. |
| Gauge | Severity colors: normal and green black, yellow and red red. The unfilled track uses the background color, so only the value arc is visible. |
| Thermostat, humidifier | Target arc red. The background track is drawn at 30% opacity and disappears. |
| History graph, statistics graph, sensor | Line series alternate red and black; the sensor card line is black. Timeline bars are red when active and black when inactive, with gaps for unavailable and unknown periods. |
| Calendar | Calendars alternate red and black. |
| Weather forecast | Sun red, clouds outlined black, rain and snow black. |
| Clock | Hands black, analog second hand red. |
| To-do list | Checkboxes outlined black, checked filled red. |
| Markdown | Black text, red links, black table borders. |
| Energy | Grid consumption, gas and water black; solar, return and battery discharge red. |
| Heading, badges | Black text and outline. |

### Tips

- Prefer cards with large text and solid shapes. Thin fonts, small icons and translucent
  backgrounds lose detail after quantization.
- Text is about 3px larger than Home Assistant's default at every size, and the thinnest font
  weight is raised. Change `ha-font-size-scale` in the theme to scale all sizes further. If text
  clips in a sections view, uncomment `ha-section-grid-row-height` for taller rows.
- With only two ink colors, charts with more than two series and calendars with more than two
  calendars repeat colors. Split them across cards when they need to stay distinguishable.
