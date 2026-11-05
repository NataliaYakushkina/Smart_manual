# Device facts: digital thermo-hygrometer

The AI assistant answers **only** from this file and the procedure files.

- Source: Natalia's own device (logo "Pro" on the metal plate on the front).

## What it is

- Indoor thermometer and hygrometer: shows the room temperature and the relative humidity.
- Size: 54.8 × 66.0 × 15.5 mm.
- Display: segment LCD on the front.

## Display (front)

Each row has a marker in the Unity prefab with the same name, so the assistant can point at it.

| Marker | What it shows | Example on the photo |
|---|---|---|
| `Display_Temperature` | Room temperature, top half of the display, one decimal place | 18.6 |
| `Display_TemperatureUnit` | Unit of the temperature, right of the number: °C or °F | °C |
| `Display_Humidity` | Relative humidity in percent, bottom half of the display | 64 % |
| `Display_ComfortIcon` | Comfort indicator: face icon, bottom left | neutral face |


## Power

- 1 × AAA battery (1.5 V).
- Battery compartment on the back. The lid slides straight down to open.
- Polarity: **+ is on the left**, seen from the open compartment with the device face-down.

## Buttons

- `UnitButton`: on the back, top right (device face-down). Press to switch the temperature unit between °C and °F.

## Mounting and placement

- Wall-mount keyhole on the back.
- There is a magnet on the back side so the thermometer can be placed on the fridge.
- Ventilation holes on the back for the sensor.

