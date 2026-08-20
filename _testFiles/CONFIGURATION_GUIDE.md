# Wyrestorm NetworkHD Essentials Configuration Guide

This guide explains the configuration example for a Wyrestorm system with NHD-120-TX transmitters and NHD-150-RX receivers.

## Configuration Overview

The example configuration includes:
- **1 NHD-CTL-PRO Controller**: Manages all devices and provides feedback
- **4 NHD-120-TX Transmitters**: HDMI input devices
- **1 NHD-150-RX Receiver**: HDMI output device with multiview support

## Device Configuration Details

### Controller (NHD-CTL-PRO)

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
        "autoReconnect": true,
        "autoReconnectIntervalMs": 10000
      }
    },
    "customMultiviewLayouts": [...],
    "multiviewPresets": [...]
  }
}
```

**Key Points:**
- **Address**: Set to your controller's IP address (192.168.1.50 in this example)
- **Port**: 23 for Telnet (default), 10022 for SSH
- **autoReconnect**: Enables automatic reconnection if the controller loses connection
- **customMultiviewLayouts**: Define custom window layouts for multiview displays
- **multiviewPresets**: Define preset configurations for quick recall

### Transmitters (NHD-120-TX)

```json
{
  "key": "nhdTx1",
  "name": "Transmitter 1",
  "type": "nhd-120-tx",
  "group": "nhd",
  "properties": {
    "matrixInputSlot": 1,
    "alias": "Tx1",
    "rs232RoutingMode": "Enabled",
    "irRoutingMode": "Enabled"
  }
}
```

**Key Points:**
- **matrixInputSlot**: Must match the transmitter's slot on the controller (1-16)
- **alias**: Must match the alias configured on the device itself in the controller settings
- **rs232RoutingMode**: Enables RS-232 routing support (Enabled, Disabled, or Default)
- **irRoutingMode**: Enables IR routing support (Enabled, Disabled, or Default)

**NHD-120-TX Features:**
- HDMI input (1x)
- Analog audio input
- Stream output port
- IR support (transmit)
- RS-232 support
- CEC support

### Receiver (NHD-150-RX)

```json
{
  "key": "nhdRx1",
  "name": "Receiver 1",
  "type": "nhd-150-rx",
  "group": "nhd",
  "properties": {
    "matrixOutputSlot": 1,
    "alias": "Rx1",
    "rs232RoutingMode": "Enabled",
    "customMultiviewLayouts": [...],
    "multiviewPresets": [...]
  }
}
```

**Key Points:**
- **matrixOutputSlot**: Must match the receiver's slot on the controller (1-16)
- **alias**: Must match the alias configured on the device itself
- **rs232RoutingMode**: Enables RS-232 routing support
- **customMultiviewLayouts**: Optional - define receiver-specific multiview layouts
- **multiviewPresets**: Optional - define receiver-specific multiview presets

**NHD-150-RX Features:**
- Stream input port (supports up to 9 simultaneous streams)
- HDMI output (1x)
- Analog audio output
- RS-232 support
- CEC support
- Multiview support with configurable layouts
- NO IR support

## Multiview Configuration

### Custom Layouts

Define window positions, sizes, and scaling for multiview displays:

```json
{
  "key": "quad-2x2",
  "displayName": "Quad 2x2",
  "mode": "Tile",
  "canvasWidth": 1920,
  "canvasHeight": 1080,
  "audioMode": "Window",
  "audioWindowReference": 1,
  "windows": [
    {
      "windowReference": 1,
      "x": 0,
      "y": 0,
      "width": 960,
      "height": 540,
      "scale": "Fit"
    },
    // ... additional windows ...
  ]
}
```

**Layout Properties:**
- **mode**: "Tile" (non-overlapping) or "Overlay" (overlapping windows)
- **canvasWidth/Height**: Display resolution (typically 1920x1080)
- **audioMode**: "Window" (audio from a window), "Separate" (audio from transmitter), or "NoChange"
- **audioWindowReference**: Window number for audio when audioMode is "Window"
- **windows**: Array of window definitions

**Window Properties:**
- **windowReference**: Unique window number (1-9 max for NHD-150-RX)
- **x, y**: Top-left corner position in pixels
- **width, height**: Window dimensions in pixels
- **scale**: "Fit" (maintain aspect ratio) or "Stretch" (fill the space)

### Presets

Define saved configurations combining layouts and routing:

```json
{
  "key": "quad-preset",
  "displayName": "Quad Layout",
  "layoutSource": "Config",
  "layout": "quad-2x2",
  "windowRoutes": [
    {
      "windowReference": 1,
      "txKey": "nhdTx1"
    },
    {
      "windowReference": 2,
      "txKey": "nhdTx2"
    },
    // ... additional window routes ...
  ],
  "audioMode": "Window",
  "audioWindowReference": 1
}
```

**Preset Properties:**
- **layoutSource**: "Config" (from customMultiviewLayouts) or "Controller" (from device)
- **layout**: Key of the layout to use
- **windowRoutes**: Map transmitters to specific windows
- **audioMode**: "Window", "Separate", or "NoChange"
- **audioWindowReference**: Window number for audio (when audioMode is "Window")
- **audioTxKey**: Transmitter key for audio (when audioMode is "Separate")

## Preset Resolution Order

When recalling a preset:
1. RX-local presets (defined in receiver properties) - checked first
2. Controller-level presets (defined in controller properties) - fallback

Local receiver definitions override controller definitions.

## Configuration Best Practices

1. **Device Aliases**: The alias in the config must exactly match the alias configured on the actual device
2. **Matrix Slots**: Each transmitter/receiver must use a unique matrix slot (1-16)
3. **IP Address**: Ensure the controller IP address is correct and accessible from the system
4. **Naming Convention**: Use descriptive keys (e.g., "nhdTx1", "nhdRx1") for easy identification
5. **Redundancy**: Configure autoReconnect on the controller for reliability
6. **Multiview Defaults**: Define at least one preset to avoid configuration issues
7. **Audio Routing**: Carefully plan audio source routing to avoid feedback and unsupported modes

## Routing Modes

- **Enabled**: Port is actively routed through the Essentials routing matrix
- **Disabled**: Port exists but is not routed
- **Default**: Uses the device's default behavior (varies by device type)

## Network Configuration

- **TCP/IP (Telnet)**: Port 23 (default)
- **SSH**: Port 10022 (requires credentials)
- **RS-232**: Direct serial connection (rarely used in modern installations)

For SSH connections, you can set credentials at:
- `control.tcpSshProperties.username` / `password` (for connection)
- `properties.apiUsername` / `apiPassword` (for API if different from connection credentials)

## Troubleshooting

### Device Not Responding
- Verify IP address and port
- Confirm device is powered on and connected to network
- Check firewall rules
- Verify autoReconnect is enabled for resilience

### Multiview Not Working
- Confirm NHD-150-RX is the target (only receiver supports multiview)
- Verify window coordinates don't overlap (if mode is "Tile")
- Check that all referenced transmitters in windowRoutes exist
- Ensure audioWindowReference exists and is valid when audioMode is "Window"

### Audio Issues
- Verify audioMode matches your use case (Window, Separate, NoChange)
- If using audioMode "Separate", ensure audioTxKey points to a valid transmitter
- Check that analog audio cables are connected
- Confirm RS-232 routing is properly configured if needed

## See Also

- [README.md](./README.md) - Main plugin documentation
- NetworkHD API documentation (in _docs folder)
