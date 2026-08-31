# BitsPleaseYT M12

BitsPleaseYT M12 is a real-time 12-band multiband compressor for Windows, macOS, and Linux. It uses eleven fourth-order Linkwitz-Riley IIR crossovers, independent compression controls for every band, global relative controls, solo/bypass, presets, and live metering.

## Windows

Run `BitsPleaseYT-M12-Windows-x64-Setup.exe`, or extract the Windows ZIP for a portable installation. The native Windows edition supports WASAPI playback capture and recording inputs.

## macOS

Open the DMG and copy **BitsPleaseYT M12** to Applications. The portable edition uses CoreAudio through PortAudio. A virtual input such as BlackHole is required for playback capture.

### macOS audio routing

macOS does not expose audio playing in Discord, a browser, or another app as a
normal input device. To process that audio, install a virtual CoreAudio device
such as [BlackHole](https://github.com/ExistentialAudio/BlackHole), route the
source app or a macOS Multi-Output Device to it, and select it as M12's source.

To send a processed microphone into Discord, select the microphone or external
audio interface as M12's source, select a virtual device as M12's processed
output, and select that virtual device as Discord's microphone.

Connected USB and Thunderbolt interfaces are listed as normal CoreAudio inputs
and outputs. If an interface is connected after M12 opens, click **Refresh** to
re-scan CoreAudio. Ensure M12 has microphone permission under **System Settings
> Privacy & Security > Microphone**.

The macOS build is ad-hoc signed because this project does not currently have an
Apple Developer Program certificate. If macOS says the app is damaged, first
confirm that you downloaded it from the official GitHub release, copy it to
Applications, and run:

```bash
xattr -dr com.apple.quarantine "/Applications/BitsPleaseYT M12.app"
open "/Applications/BitsPleaseYT M12.app"
```

This removes Apple's download quarantine from this app only. A fully notarized,
warning-free installer requires a paid Apple Developer Program membership.

## Linux

Mark the AppImage executable and run it:

```bash
chmod +x BitsPleaseYT-M12-Linux-*.AppImage
./BitsPleaseYT-M12-Linux-*.AppImage
```

A tarball is also supplied. PipeWire/PulseAudio monitor sources can provide playback capture.

## Build

```powershell
dotnet build MultibandCore.csproj
dotnet build portable/BitsPleaseYTM12.Portable.csproj
```

Both projects read their version from `Directory.Build.props`. For a future release from a clean working tree, run:

```powershell
.\scripts\Release.ps1 -Version 1.0.1
```

The script updates shared and macOS versions, validates both builds, commits, tags, and pushes. Tagged builds automatically publish installers and portable packages on GitHub Releases.
