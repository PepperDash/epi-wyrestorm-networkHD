
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
