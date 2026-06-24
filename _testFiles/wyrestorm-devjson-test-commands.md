
## API Commands (20) - Telnet Syntax

### Matrix Routing Commands
```
matrix video set <TX> <RX>              # Route video from TX to RX
matrix audio set <TX> <RX>              # Route audio from TX to RX
matrix infrared set <TX> <RX>           # Route IR from TX to RX
matrix serial set <TX> <RX>             # Route RS-232 from TX to RX
matrix video set null <RX>              # Clear video route from RX
matrix audio set null <RX>              # Clear audio route from RX
```

### Multiview Commands
```
mview get <RX>                          # Query multiview state
mview set <RX> tile <TX:X_Y_W_H:SCALE> # Set tile with TX source
mscene active <RX> <layout>             # Apply multiview layout to RX
```

### Device Control Commands
```
config set device reboot <TX|RX>        # Reboot device
config set device sinkpower on <RX>     # Power on display via RX
config set device sinkpower off <RX>    # Power off display via RX
config set device audio volume up/down/mute/unmute analog <RX>
```

### Query/Get Commands
```
config get version                       # Get system version
config get ipsetting                     # Get network config
```

## Devjson Command Format

**Format:** `{"deviceKey":"<key>","methodName":"<method>","params":[...]}`

Parameters use `eRoutingSignalType` enum values: `AudioVideo`, `Video`, `Audio`

## Devjson Command Lines

**Basic Device Control:**
```json
devjson:1 {"deviceKey":"nhdCtl","methodName":"ExecuteSwitch","params":["nhdTx1","nhdRx1","AudioVideo"]}
devjson:1 {"deviceKey":"nhdCtl","methodName":"ExecuteSwitch","params":["nhdTx1","nhdRx1","Video"]}
devjson:1 {"deviceKey":"nhdCtl","methodName":"ExecuteSwitch","params":["nhdTx1","nhdRx1","Audio"]}
```

**Multiview Tile Routing (Primary Method):**
```json
devjson:1 {"deviceKey":"NhdRouter","methodName":"RouteMVTile","params":["nhdTx1","nhdRx1","4-1",1]}
devjson:1 {"deviceKey":"NhdRouter","methodName":"RouteMVTile","params":["nhdTx1","nhdRx1","4-1",2]}
devjson:1 {"deviceKey":"NhdRouter","methodName":"RouteMVTile","params":["nhdTx1","nhdRx1","4-1",3]}
devjson:1 {"deviceKey":"NhdRouter","methodName":"RouteMVTile","params":["nhdTx1","nhdRx1","4-1",4]}
```

**Multiview Fullscreen Control:**
```json
devjson:1 {"deviceKey":"NhdRouter","methodName":"FullscreenMVTile","params":["nhdRx1",1]}
devjson:1 {"deviceKey":"NhdRouter","methodName":"FullscreenMVTile","params":["nhdRx1",2]}
devjson:1 {"deviceKey":"NhdRouter","methodName":"FullscreenMVTile","params":["nhdRx1",3]}
devjson:1 {"deviceKey":"NhdRouter","methodName":"FullscreenMVTile","params":["nhdRx1",4]}
```

**Multiview Layout Control:**
```json
devjson:1 {"deviceKey":"NhdRouter","methodName":"ActivateMVLayout","params":["nhdRx1","4-1"]}
devjson:1 {"deviceKey":"NhdRouter","methodName":"ActivateMVLayout","params":["nhdRx1","fullscreen"]}
```

**Multiview Layout Query & Recall (RX device):**
```json
# Refresh the layout list from the controller (mscene get), then query it with ids
devjson:1 {"deviceKey":"nhdRx1","methodName":"RefreshMVLayouts","params":[]}
devjson:1 {"deviceKey":"nhdRx1","methodName":"GetMVLayoutsWithIdsJson","params":[]}

# Recall a layout by the id returned from the query above.
# Accepts the raw layoutId ("2-1", "test1"), the prefixed id ("preset:2-1"), or "custom:<key>".
devjson:1 {"deviceKey":"nhdRx1","methodName":"RecallMVLayout","params":["2-1"]}
devjson:1 {"deviceKey":"nhdRx1","methodName":"RecallMVLayout","params":["preset:2-1"]}
devjson:1 {"deviceKey":"nhdRx1","methodName":"RecallMVLayout","params":["test1"]}
```

**Config-Driven Custom Layouts (RX device — `customMultiviewLayouts`):**
```json
# Keys come from customMultiviewLayouts. Resolution order: RX-local first,
# then CTL-level fallback (RX overrides CTL when the same key is defined on both).
devjson:1 {"deviceKey":"nhdRx1","methodName":"ApplyCustomMVLayout","params":["fullscreen"]}
devjson:1 {"deviceKey":"nhdRx1","methodName":"ApplyCustomMVLayout","params":["pip-main"]}
```

**Config-Driven Presets (RX device — `multiviewPresets`):**
```json
# Keys come from multiviewPresets (layout + optional window routing/audio policy).
# "4-tile" / "fullscreen-custom" are defined on the CTL and shared by every RX.
# "fullscreen" is overridden locally on nhdRx1, so the RX definition wins.
devjson:1 {"deviceKey":"nhdRx1","methodName":"ApplyMVPreset","params":["4-tile"]}
devjson:1 {"deviceKey":"nhdRx1","methodName":"ApplyMVPreset","params":["fullscreen-custom"]}
devjson:1 {"deviceKey":"nhdRx1","methodName":"ApplyMVPreset","params":["fullscreen"]}

# A different RX (e.g. nhdRx2) with no local override resolves the same keys
# from the CTL-level definitions.
devjson:1 {"deviceKey":"nhdRx2","methodName":"ApplyMVPreset","params":["4-tile"]}
devjson:1 {"deviceKey":"nhdRx2","methodName":"ApplyMVPreset","params":["fullscreen-custom"]}
```

**Device Methods via CTL (Telnet API for direct device control):**
```
Telnet: matrix video set Tx1 Rx1
Telnet: matrix audio set Tx1 Rx1
Telnet: config set device sinkpower on Rx1
Telnet: config set device sinkpower off Rx1
Telnet: config set device audio volume up analog Rx1
Telnet: config set device audio volume mute analog Rx1
Telnet: config get version
Telnet: config get ipsetting
