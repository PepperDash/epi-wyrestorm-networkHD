![PepperDash Essentials Plugin Logo](/images/essentials-plugin-blue.png)

# WyreStorm NetworkHD Essentials Plugin

Essentials plugin for WyreStorm NetworkHD endpoints and controller-driven matrix routing.

## License

Provided under MIT license. See [LICENSE.md](LICENSE.md).

## Overview

This plugin provides:

- Device support for WyreStorm NetworkHD controller, transmitter, and receiver models in this repository.
- Matrix routing integration through a global router instance.
- Route and device feedback tracking across video, audio, and USB domains.
- Multiview layout control and tracking for supported decoders.
- Session-aware CTL command handling (login/readiness, queueing, throttled refresh, and feedback parsing).

## Supported Device Types

The following Essentials type names are currently registered:

| Device | Type Names | Notes |
| --- | --- | --- |
| NHD-CTL-PRO | nhd-ctl-pro, nhdctlpro | Controller transport, session lifecycle, matrix and notification parsing |
| NHD-120-TX | nhd-120-tx, nhd120tx | Transmitter with HDMI input, analog audio input, stream output |
| NHD-150-RX | nhd-150-rx, nhd150rx | Receiver with stream input, HDMI output, analog audio output, multiview support |



## Requirements

- .NET SDK that can build net8 projects.
- PepperDash Essentials environment for runtime deployment.
- Declared minimum Essentials framework version in factories: 3.0.0.
- Current package reference in this repository: PepperDashEssentials 3.0.0-dev-v3-testing.13.

## Build and Package

From repository root:

1. Restore packages:
	 dotnet restore .\epi-wyrestorm-networkHD.4Series.sln
2. Build:
	 dotnet build .\epi-wyrestorm-networkHD.4Series.sln -c Debug

On build, the project generates a CPLZ package in the output folder:

- output/epi-wyrestorm-networkHD.4Series.<Version>.cplz

Version and package metadata are defined in:

- src/Directory.Build.props
- src/epi-wyrestorm-networkHD.4Series.csproj
<br>
## Configuration

### Controller (NHD-CTL-PRO)

The CTL device is required for command transport and feedback parsing.

Example skeleton:

```json
{
	"key": "nhdCtl",
	"name": "NHD Controller",
	"type": "nhd-ctl-pro",
	"group": "nhd",
	"properties": {
        "control": {
            "method": "tcpIp",
            "tcpSshProperties": {
                "address": "192.168.1.50",
                "port": 23,
                "autoReconnect": true,,
                "autoReconnectIntervalMs": 10000,
            }
        },
        "customMultiviewLayouts": [],
        "multiviewPresets": []
    }
}
```

Notes:

- The CTL can be controlled over RS-232, Telnet over TCP/IP (port 23) or SSH (port 10022).
- Username/password credentials are only required for SSH.
- For SSH, API credentials can be set directly on properties.apiUsername / properties.apiPassword. If those values are omitted, this plugin also hydrates credentials from control.tcpSshProperties username/password.
<br>

### Transmitter (NHD-120-TX)

```json
{
	"key": "nhdTx1",
	"name": "TX 01",
	"type": "nhd-120-tx",
	"group": "nhd",
	"properties": {
		"matrixInputSlot": 1,
		"alias": "Tx1"
	}
}
```
<br>

### Receiver (NHD-150-RX)

```json
{
	"key": "nhdRx1",
	"name": "RX 01",
	"type": "nhd-150-rx",
	"group": "nhd",
	"properties": {
		"matrixOutputSlot": 1,
		"alias": "nhdRx1",
        "customMultiviewLayouts": [],
        "multiviewPresets": []
	}
}
```
<br>

### Core Properties Reference

| Property | Type | Applies To | Description |
| --- | --- | --- | --- |
| matrixInputSlot | number | TX | Matrix input slot number used for transmitter routing |
| matrixOutputSlot | number | RX | Matrix output slot number used for receiver routing |
| alias | string | TX, RX | Must match the endpoint's defined alias for CTL/API commands |
| apiUsername | string | CTL | API login username |
| apiPassword | string | CTL | API login password |
| customMultiviewLayouts | array | RX, CTL | Named custom multiview geometry profiles |
| multiviewPresets | array | RX, CTL | Named preset workflows (layout + optional window routing/audio policy) |


<br>

## Multiview

NHD-150-RX supports multiview (max stream count 9 in this plugin).

### Custom Layout Example

```json
"customMultiviewLayouts": [
	{
		"key": "quad",
		"displayName": "Quad 2x2",
		"mode": "Tile",
		"canvasWidth": 1920,
		"canvasHeight": 1080,
		"audioMode": "Window",
		"audioWindowReference": 1,
		"windows": [
			{ "windowReference": 1, "x": 0,   "y": 0,   "width": 960, "height": 540, "scale": "Fit" },
			{ "windowReference": 2, "x": 960, "y": 0,   "width": 960, "height": 540, "scale": "Fit" },
			{ "windowReference": 3, "x": 0,   "y": 540, "width": 960, "height": 540, "scale": "Fit" },
			{ "windowReference": 4, "x": 960, "y": 540, "width": 960, "height": 540, "scale": "Fit" }
		]
	}
]
```
### Preset Example

```json
"multiviewPresets": [
	{
		"key": "quad-default",
		"displayName": "Quad Default",
		"layoutSource": "Config",
		"layout": "quad",
		"windowRoutes": [
			{ "windowReference": 1, "txKey": "nhdTx1" },
			{ "windowReference": 2, "txKey": "nhdTx2" },
			{ "windowReference": 3, "txKey": "nhdTx3" },
			{ "windowReference": 4, "txKey": "nhdTx4" }
		],
		"audioMode": "Window",
		"audioWindowReference": 1
	}
]
```

### Preset Notes

| Item | Values | Notes |
| --- | --- | --- |
| layoutSource | Controller, Config | Controller - built in or customn layout stored on CTL<br> Config - from customMultiviewLayouts |
| mode | Tile, Overlay | Tile - Windows defined in config will not overlap each other <br> Overlay - Windows defined in config will overlap each other <br><br> Note: setting this incorrectly mave have adverse effects on rendering content |
| scale | Fit, Stretch | Window scaling options |
| audioMode | Window, Separate, NoChange | Window - select a window index to be the audio source <br> Separate - select a transmitter endpoint to be the audio source <br> NoChange - do not make an audio selection when recalling this Preset |
| audioWindowReference | Integer index of selected tile in Preset | Must be defined when audioMode is set to Window |
| audioTxKey | Transmitter device key  | must be defined when audioMode is set to Separate |
| preset resolution order | RX-local first, then controller-level fallback | Local receiver definitions override controller definitions |

Note: When multiple multiview receivers in a system may use the same layouts/presets, it may be easier to define them in once the CTL device so they can be shared without data replication in config. When presets or layouts with the same key are defined in both the CTL device and the Rx device, the Rx device takes precedence. 

<br>

## Routing and Feedback Behavior

- A global router singleton is auto-registered under key NhdRouter.
- Matrix clear route aliases accepted by router input resolution: none, null, off, $off.
- Single-stream matrix switching is enforced only for outputs that support it.
	- Multiview outputs are excluded from single-stream matrix switching.
- CTL session bootstrap requests matrix state, device list, device status, and endpoint notifications.
- Matrix state refresh is request-driven and includes a periodic refresh every 30 seconds when session-ready and connected.
- Matrix refresh requests are throttled (2 seconds).
- Video lost notifications are debounced (10 seconds) to avoid sync flap churn.

<br>

## NhdRouter Basic Route Commands

| Function | Signature | Notes |
| --- | --- | --- |
| Route | `void Route(string inputSlotKey, string outputSlotKey, eRoutingSignalType type)` | Primary matrix routing entry point on NhdRouter |
| Route by slot | `void RouteBySlot(int inputSlot, int outputSlot, eRoutingSignalType type)` | Routes using configured `matrixInputSlot` and `matrixOutputSlot` values |
| Clear route | `Route(clearAlias, outputSlotKey, type)` | clearAlias values: none, null, off, $off |

Note: These methods are invoked on the NdhRouter device <br> `type` allowed values are `eRoutingSignalType.AudioVideo`, `eRoutingSignalType.Video`, `eRoutingSignalType.Audio`


<br>

## Multiviewer Specific Comands

Common endpoint methods available through device classes include:

| Function | MV Rx Endpoint Method | Notes |
| --- | --- | --- |
| Route MV tile with layout | `bool RouteMVTile(string inputSlotKey, string outputSlotKey, string layoutName, int tileReference)` | Routes a source slot to a specific MV tile within the named layout |
| Reprobe layouts | `bool ReprobeMVLayouts()` | queries an endpoint for its preconfigured and custom layouts defined in the CTL |
| Apply custom layout | `bool ApplyCustomMVLayout(string layoutKey)` |  |
| Apply custom layout with sources | `bool ApplyCustomMVLayoutWithSources(string layoutKey, IDictionary<int, string> sourceReferencesByWindow)` | used by the apply preset function |
| Apply preset | `bool ApplyMVPreset(string presetKey)` |  |
| Fullscreen tile | `bool FullscreenMVTile(int sourceTileReference)` |  |
| Return from fullscreen | `bool ReturnFromMVFullscreen()` |  |

These ultimately execute through the CTL session manager command pipeline.

<br>

## Troubleshooting

- No routing/control response:
	- Verify an NHD-CTL-PRO device is present and connected.
	- Verify control transport settings and credentials.
- Session never reaches ready:
	- Check for User:/Password: prompts and configure apiUsername/apiPassword.
	- Telnet credential prompts are handled automatically by the plugin.
- Feedback appears delayed after external matrix changes:
	- This plugin applies a periodic matrix state check (30 seconds) plus event-driven refreshes.
- Multiview preset/layout rejects:
	- Validate layout keys, window references (1-based), and transmitter device keys.

<br>

## Repository Layout

- src/: plugin source
- _docs/: vendor API and technical reference notes
- images/: repository images
- output/: generated local build/package artifacts

<br>

## Contributing

Issues and pull requests are welcome. Please include:

- Firmware/build context
- Device model(s)
- Relevant config snippets
- Log excerpts showing the command/response sequence

<br>

## Development Note

IR/232 functionality is currently under development and intentionally omitted from this README for now.
