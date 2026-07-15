
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

### Dynamic Multiview Layout (computed at runtime, not from a named config layout)
```
# Same wire format as "mview set" custom layouts, but the tile geometry is computed on the fly
# from a set of sources + priorities (NhdDynamicMultiviewLayoutCalculator) rather than read from
# customMultiviewLayouts config. Sent by Nhd150Rx.ApplyDynamicLayout via
# NhdCtlSessionManager.TryApplyDynamicLayout.
mview set <RX> tile <TX:X_Y_W_H:SCALE> <TX:X_Y_W_H:SCALE> ...
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

**Dynamic Multiview Layout (RX device — computed at runtime from sources + priority):**
```json
# Builds a layout on the fly via NhdDynamicMultiviewLayoutCalculator instead of a named
# customMultiviewLayouts/multiviewPresets entry. Uses the devjson-friendly overload of
# ApplyDynamicLayout, which takes parallel primitive arrays (sourceKeys[i] <-> priorities[i], lower
# number = higher priority) plus the presentation source's TX device key ("" if none active) -
# devjson's reflection-based dispatcher can't construct the ParticipantSource POCO overload
# directly from JSON. Tile count is capped by the RX's ConfiguredMaxTileCount (see "maxTileCount"
# in config, defaults to MaxStreamCount) - lowest-priority sources beyond capacity are dropped.

# No presentation active: even grid, ordered by priority.
devjson:1 {"deviceKey":"nhdRx1","methodName":"ApplyDynamicLayout","params":[["nhdTx1","nhdTx2"],[0,1],""]}

# Presentation active: tile 1 is the (larger) presentation source; remaining sources fill an
# equal-size thumbnail strip, ordered by priority.
devjson:1 {"deviceKey":"nhdRx1","methodName":"ApplyDynamicLayout","params":[["nhdTx1","nhdTx2"],[0,1],"nhdTx3"]}
```

**Per-Tile Ad-Hoc Routing (RX device — individual `NhdMultiviewTileSink` child devices):**
```
# Each tile also exists as its own routable Essentials sink (key: "<rxKey>-tile<N>", 1-based),
# independent of ApplyDynamicLayout above. It's registered with DeviceManager and routable through
# the standard Essentials routing framework (tie lines / IRunDirectRouteAction / ReleaseAndMakeRoute),
# e.g. from room code:
#   someRoom.RunDirectRoute("nhdTx1", "nhdRx1-tile1", eRoutingSignalType.Video);
# It's also reachable programmatically via the parent RX (which implements IRoutingSinkWithLayouts):
#   var rx = DeviceManager.GetDeviceForKey("nhdRx1") as IRoutingSinkWithLayouts;
#   var tileSink = rx?.WindowTileSinks[1] as IRoutingSinkWithFeedback;
# Not devjson-testable directly - ExecuteSwitch/SetCurrentSource take routing-framework object
# types (RoutingInputPort selectors / IRoutingSource), not devjson-serializable primitives.
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
