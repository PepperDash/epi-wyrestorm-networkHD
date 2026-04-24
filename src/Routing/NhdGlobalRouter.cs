using System;
using System.Collections.Generic;
using System.Linq;
using PepperDash.Core;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;
using PepperDash.Essentials.Plugin.Comms;

namespace PepperDash.Essentials.Plugin.Routing;

public class NhdGlobalRouter : EssentialsDevice, IRoutingNumeric, IMatrixRouting
{
    private static readonly NhdGlobalRouter _instance = new();
    private readonly NhdPrimaryStreamDomainRouter _primaryStreamRouter;
    private readonly NhdUsbDomainRouter _usbRouter;
    private readonly NhdControlDomainRouter _controlRouter;

    public const string InstanceKey = "NhdRouter";
    public const string RouteOff = "$off";
    public const string NoSourceText = "No Source";

    private NhdGlobalRouter()
        : base(InstanceKey)
    {
        _primaryStreamRouter = new NhdPrimaryStreamDomainRouter();
        _usbRouter = new NhdUsbDomainRouter();
        _controlRouter = new NhdControlDomainRouter();

        InputPorts = new RoutingPortCollection<RoutingInputPort>();
        OutputPorts = new RoutingPortCollection<RoutingOutputPort>();

        InputSlots = new Dictionary<string, IRoutingInputSlot>();
        OutputSlots = new Dictionary<string, IRoutingOutputSlot>();

        AddPostActivationAction(BuildMatrixRouting);
        AddPostActivationAction(BuildTieLines);
    }

    public static NhdGlobalRouter Instance => _instance;

    public RoutingPortCollection<RoutingInputPort> InputPorts { get; private set; }
    public RoutingPortCollection<RoutingOutputPort> OutputPorts { get; private set; }

    public Dictionary<string, IRoutingInputSlot> InputSlots { get; private set; }
    public Dictionary<string, IRoutingOutputSlot> OutputSlots { get; private set; }

    public static string GetRouterInputPortKeyForEndpointPort(string endpointKey, string endpointPortKey)
    {
        if (string.IsNullOrWhiteSpace(endpointKey))
            return null;

        if (string.IsNullOrWhiteSpace(endpointPortKey)
            || endpointPortKey.Equals(NhdPortKeys.Stream, StringComparison.OrdinalIgnoreCase))
            return endpointKey;

        return string.Format("{0}-in-{1}", endpointKey, endpointPortKey);
    }

    public static string GetRouterOutputPortKeyForEndpointPort(string endpointKey, string endpointPortKey)
    {
        if (string.IsNullOrWhiteSpace(endpointKey))
            return null;

        if (string.IsNullOrWhiteSpace(endpointPortKey)
            || endpointPortKey.Equals(NhdPortKeys.Stream, StringComparison.OrdinalIgnoreCase))
            return endpointKey;

        return string.Format("{0}-out-{1}", endpointKey, endpointPortKey);
    }

    public void ExecuteSwitch(object inputSelector, object outputSelector, eRoutingSignalType signalType)
    {
        if (outputSelector is not NhdMatrixOutput output)
        {
            this.LogError("Output selector is not NhdMatrixOutput");
            return;
        }

        if (inputSelector is not IRoutingInputSlot inputSlot)
        {
            this.LogError("Input selector is not IRoutingInputSlot");
            return;
        }

        var handled = false;

        if (_primaryStreamRouter.TryExecute(this, inputSlot, output, signalType, out var primarySignalType))
        {
            handled = true;
            SetTrackedOutputRoutes(output, inputSlot, primarySignalType);
        }

        if (_usbRouter.TryExecute(this, inputSlot, output, signalType))
        {
            handled = true;
            SetTrackedOutputRoutes(output, inputSlot, NhdRoutingSignalTypes.UsbInput | NhdRoutingSignalTypes.UsbOutput | eRoutingSignalType.Usb);
        }

        if (_controlRouter.TryExecute(this, inputSlot, output, signalType))
        {
            handled = true;

            var controlSignalType = (eRoutingSignalType)0;
            if (signalType.HasFlag(NhdRoutingSignalTypes.Ir))
                controlSignalType |= NhdRoutingSignalTypes.Ir;

            if (signalType.HasFlag(NhdRoutingSignalTypes.Serial))
                controlSignalType |= NhdRoutingSignalTypes.Serial;

            if (controlSignalType != 0)
                SetTrackedOutputRoutes(output, inputSlot, controlSignalType);
        }

        if (!handled)
            this.LogError("Unsupported signal type '{signalType}' for matrix command", signalType);
    }

    public void ExecuteNumericSwitch(ushort input, ushort output, eRoutingSignalType type)
    {
        throw new NotImplementedException("ExecuteNumericSwitch");
    }

    public bool TrySetTrackedMatrixRoute(string txEndpointKey, string rxEndpointKey, eRoutingSignalType signalType)
    {
        if (string.IsNullOrWhiteSpace(rxEndpointKey))
            return false;

        if (!OutputSlots.TryGetValue(rxEndpointKey, out var outputSlot))
            return false;

        if (outputSlot is not NhdMatrixOutput output)
            return false;

        IRoutingInputSlot inputSlot = null;
        if (!string.IsNullOrWhiteSpace(txEndpointKey))
        {
            if (!InputSlots.TryGetValue(txEndpointKey, out inputSlot))
                return false;
        }

        SetTrackedOutputRoutes(output, inputSlot, signalType);
        return true;
    }

    public void Route(string inputSlotKey, string outputSlotKey, eRoutingSignalType type)
    {
        if (!InputSlots.TryGetValue(inputSlotKey, out var inputSlot))
        {
            this.LogError("Unable to find input slot with key {0}", inputSlotKey);
            return;
        }

        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find output slot with key {0}", outputSlotKey);
            return;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            Debug.LogMessage(Serilog.Events.LogEventLevel.Error, "Output with key {key} is not NhdMatrixOutput", this, outputSlotKey);
            return;
        }

        ExecuteSwitch(inputSlot, output, type);
    }

    public bool ActivateMultiviewLayout(string outputSlotKey, string layoutName)
    {
        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview preset layouts", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview layout activation");
            return false;
        }

        return ctl.SessionManager.TryActivateMultiviewLayout(this, output.Device, layoutName);
    }

    public bool TryGetTrackedMultiviewLayout(string outputSlotKey, out string layoutName, out bool inferred)
    {
        layoutName = null;
        inferred = false;

        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview preset layouts", output.Device.Key);
            return false;
        }

        layoutName = output.Device.ActivePresetMultiviewLayoutName;
        inferred = output.Device.ActivePresetMultiviewLayoutInferred;
        return !string.IsNullOrWhiteSpace(layoutName);
    }

    public bool ProbeAndLearnMultiviewLayouts(string outputSlotKey)
    {
        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview preset layouts", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview layout probing");
            return false;
        }

        return ctl.SessionManager.TryProbeAndLearnMultiviewLayouts(this, output.Device);
    }

    public bool RouteMultiviewTile(string inputSlotKey, string outputSlotKey, int tileReference)
    {
        return RouteMultiviewTile(inputSlotKey, outputSlotKey, null, tileReference);
    }

    public bool FullscreenMultiviewTile(string outputSlotKey, int sourceTileReference)
    {
        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview fullscreen");
            return false;
        }

        return ctl.SessionManager.TryFullscreenMultiviewTile(this, output.Device, sourceTileReference);
    }

    public bool ReturnFromMultiviewFullscreen(string outputSlotKey)
    {
        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview fullscreen return");
            return false;
        }

        return ctl.SessionManager.TryReturnFromMultiviewFullscreen(this, output.Device);
    }

    public bool TryGetMultiviewFullscreenReturnLayout(string outputSlotKey, out string layoutName)
    {
        layoutName = null;

        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for multiview fullscreen return query");
            return false;
        }

        return ctl.SessionManager.TryGetMultiviewFullscreenReturnLayout(output.Device, out layoutName);
    }

    public bool RouteMultiviewTile(string inputSlotKey, string outputSlotKey, string layoutName, int tileReference)
    {
        if (!InputSlots.TryGetValue(inputSlotKey, out var inputSlot))
        {
            this.LogError("Unable to find multiview input slot with key {0}", inputSlotKey);
            return false;
        }

        if (!OutputSlots.TryGetValue(outputSlotKey, out var outputSlot))
        {
            this.LogError("Unable to find multiview output slot with key {0}", outputSlotKey);
            return false;
        }

        if (inputSlot is not NhdMatrixInput input)
        {
            this.LogError("Input slot with key {0} is not NhdMatrixInput", inputSlotKey);
            return false;
        }

        if (outputSlot is not NhdMatrixOutput output)
        {
            this.LogError("Output slot with key {0} is not NhdMatrixOutput", outputSlotKey);
            return false;
        }

        if (!output.Device.SupportsMultiview)
        {
            this.LogError("Endpoint '{key}' does not support multiview tile routing", output.Device.Key);
            return false;
        }

        var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
        if (ctl?.SessionManager == null)
        {
            this.LogError("NHD-CTL session manager is not available for guarded multiview tile routing");
            return false;
        }

        var sentImmediately = ctl.SessionManager.TryRouteMultiviewTile(this, input.Device, output.Device, layoutName, tileReference);

        if (!sentImmediately)
        {
            this.LogInformation(
                "Multiview tile route queued pending state verification. tx='{tx}', rx='{rx}', layout='{layout}', tile={tile}",
                input.Device.Key,
                output.Device.Key,
                string.IsNullOrWhiteSpace(layoutName) ? output.Device.ActivePresetMultiviewLayoutName : layoutName,
                tileReference);
        }

        return sentImmediately;
    }

    private void BuildMatrixRouting()
    {
        try
        {
            InputSlots = DeviceManager
                .AllDevices.OfType<NhdBaseDevice>()
                .Where(d => d.IsTransmitter)
                .Select(d => new NhdMatrixInput(d))
                .Cast<IRoutingInputSlot>()
                .ToDictionary(i => i.Key, i => i);

            var clearInput = new NhdMatrixClearInput();
            InputSlots.Add(clearInput.Key, clearInput);

            this.LogDebug("Total inputs: {count}", InputSlots.Count);

            OutputSlots = DeviceManager
                .AllDevices.OfType<NhdBaseDevice>()
                .Where(d => !d.IsTransmitter)
                .Select(d => new NhdMatrixOutput(d))
                .Cast<IRoutingOutputSlot>()
                .ToDictionary(o => o.Key, o => o);

            this.LogDebug("Total outputs: {count}", OutputSlots.Count);

            // Build router ports so tie lines can connect to them for each available endpoint routing port.
            foreach (var tx in DeviceManager.AllDevices.OfType<NhdBaseDevice>().Where(d => d.IsTransmitter))
            {
                foreach (var endpointOutputPort in tx.OutputPorts)
                {
                    var routerPortKey = GetRouterInputPortKeyForEndpointPort(tx.Key, endpointOutputPort.Key);
                    if (string.IsNullOrWhiteSpace(routerPortKey))
                        continue;

                    if (InputPorts.Any(p => string.Equals(p.Key, routerPortKey, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    InputPorts.Add(new RoutingInputPort(
                        routerPortKey,
                        endpointOutputPort.Type,
                        endpointOutputPort.ConnectionType,
                        tx,
                        this));
                }
            }

            foreach (var rx in DeviceManager.AllDevices.OfType<NhdBaseDevice>().Where(d => !d.IsTransmitter))
            {
                foreach (var endpointInputPort in rx.InputPorts)
                {
                    var routerPortKey = GetRouterOutputPortKeyForEndpointPort(rx.Key, endpointInputPort.Key);
                    if (string.IsNullOrWhiteSpace(routerPortKey))
                        continue;

                    if (OutputPorts.Any(p => string.Equals(p.Key, routerPortKey, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    OutputPorts.Add(new RoutingOutputPort(
                        routerPortKey,
                        endpointInputPort.Type,
                        endpointInputPort.ConnectionType,
                        rx,
                        this));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogMessage(ex, "Exception building MatrixRouting: {message}", this, ex.Message);
        }
    }

    private static void BuildTieLines()
    {
        try
        {
            var transmitters = DeviceManager.AllDevices.OfType<NhdBaseDevice>().Where(d => d.IsTransmitter).ToList();
            NhdTieLineConnector.AddTieLinesForTransmitters(transmitters);

            var receivers = DeviceManager.AllDevices.OfType<NhdBaseDevice>().Where(d => !d.IsTransmitter).ToList();
            NhdTieLineConnector.AddTieLinesForReceivers(receivers);
        }
        catch (Exception ex)
        {
            Debug.LogMessage(ex, "Exception building tie lines: {message}", null, ex.Message);
        }
    }

    private static void SetTrackedOutputRoutes(NhdMatrixOutput output, IRoutingInputSlot inputSlot, eRoutingSignalType signalType)
    {
        if (output == null)
            return;

        var hasVideo = signalType.HasFlag(eRoutingSignalType.Video);
        var hasAudio = signalType.HasFlag(eRoutingSignalType.Audio);
        if (signalType == eRoutingSignalType.AudioVideo || (hasVideo && hasAudio))
        {
            output.SetInputRoute(eRoutingSignalType.Video, inputSlot);
            output.SetInputRoute(eRoutingSignalType.Audio, inputSlot);
        }
        else
        {
            if (hasVideo)
                output.SetInputRoute(eRoutingSignalType.Video, inputSlot);

            if (hasAudio)
                output.SetInputRoute(eRoutingSignalType.Audio, inputSlot);
        }

        if (
            signalType.HasFlag(NhdRoutingSignalTypes.UsbInput)
            || signalType.HasFlag(NhdRoutingSignalTypes.UsbOutput)
            || signalType.HasFlag(eRoutingSignalType.Usb))
        {
            output.SetInputRoute(NhdRoutingSignalTypes.UsbInput, inputSlot);
            output.SetInputRoute(NhdRoutingSignalTypes.UsbOutput, inputSlot);

            if (Enum.IsDefined(typeof(eRoutingSignalType), "Usb"))
                output.SetInputRoute(eRoutingSignalType.Usb, inputSlot);
        }

        if (signalType.HasFlag(NhdRoutingSignalTypes.Ir))
            output.SetInputRoute(NhdRoutingSignalTypes.Ir, inputSlot);

        if (signalType.HasFlag(NhdRoutingSignalTypes.Serial))
            output.SetInputRoute(NhdRoutingSignalTypes.Serial, inputSlot);
    }

    private static string GetTxReference(IRoutingInputSlot inputSlot)
    {
        return inputSlot is NhdMatrixInput matrixInput
            ? matrixInput.Device.ApiEndpointReference
            : "null";
    }

    private sealed class NhdPrimaryStreamDomainRouter
    {
        public bool TryExecute(IKeyed source, IRoutingInputSlot inputSlot, NhdMatrixOutput output, eRoutingSignalType signalType, out eRoutingSignalType routedSignalType)
        {
            routedSignalType = 0;

            if (!TryResolveStreamSignalType(signalType, out routedSignalType))
                return false;

            var txRef = GetTxReference(inputSlot);
            var rxRef = output.Device.ApiEndpointReference;
            var prefix = routedSignalType == eRoutingSignalType.AudioVideo
                ? "matrix set"
                : routedSignalType == eRoutingSignalType.Video
                    ? "matrix video set"
                    : "matrix audio set";

            NhdApiCommandSender.TrySend(source, $"{prefix} {txRef} {rxRef}");
            return true;
        }

        private static bool TryResolveStreamSignalType(eRoutingSignalType signalType, out eRoutingSignalType streamType)
        {
            streamType = 0;

            if (signalType == eRoutingSignalType.AudioVideo)
            {
                streamType = eRoutingSignalType.AudioVideo;
                return true;
            }

            var hasVideo = signalType.HasFlag(eRoutingSignalType.Video);
            var hasAudio = signalType.HasFlag(eRoutingSignalType.Audio);

            if (hasVideo && hasAudio)
            {
                streamType = eRoutingSignalType.AudioVideo;
                return true;
            }

            if (hasVideo)
            {
                streamType = eRoutingSignalType.Video;
                return true;
            }

            if (hasAudio)
            {
                streamType = eRoutingSignalType.Audio;
                return true;
            }

            return false;
        }
    }

    private sealed class NhdUsbDomainRouter
    {
        public bool TryExecute(IKeyed source, IRoutingInputSlot inputSlot, NhdMatrixOutput output, eRoutingSignalType signalType)
        {
            if (!HasUsbSignal(signalType))
                return false;

            var txRef = GetTxReference(inputSlot);
            var rxRef = output.Device.ApiEndpointReference;

            NhdApiCommandSender.TrySend(source, $"matrix usb set {txRef} {rxRef}");
            return true;
        }

        private static bool HasUsbSignal(eRoutingSignalType signalType)
        {
            return signalType.HasFlag(NhdRoutingSignalTypes.UsbInput)
                || signalType.HasFlag(NhdRoutingSignalTypes.UsbOutput)
                || signalType.HasFlag(eRoutingSignalType.Usb);
        }
    }

    private sealed class NhdControlDomainRouter
    {
        public bool TryExecute(IKeyed source, IRoutingInputSlot inputSlot, NhdMatrixOutput output, eRoutingSignalType signalType)
        {
            var handled = false;
            var txRef = GetTxReference(inputSlot);
            var rxRef = output.Device.ApiEndpointReference;

            if (signalType.HasFlag(NhdRoutingSignalTypes.Ir))
            {
                NhdApiCommandSender.TrySend(source, $"matrix infrared set {txRef} {rxRef}");
                handled = true;
            }

            if (signalType.HasFlag(NhdRoutingSignalTypes.Serial))
            {
                NhdApiCommandSender.TrySend(source, $"matrix serial set {txRef} {rxRef}");
                handled = true;
            }

            return handled;
        }
    }
}
