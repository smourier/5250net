# 5250net

An independent IBM i TN5250 terminal, written in C# as a standard .NET 10 console application.
MIT licensed. No NuGet package dependencies. Native AOT publishing is enabled.

This is an initial implementation of the core display protocol. It is not a port of another terminal emulator.
It has not yet been validated against a live IBM i system and does not claim full ACS compatibility.

## Build and run

Install the .NET 10 SDK. From the repository root, run the following commands.

```powershell
dotnet build 5250net.slnx
dotnet run --project src\Terminal5250 -- --help
dotnet run --project src\Terminal5250 -- myibmi.example.com
dotnet run --project src\Terminal5250 -- myibmi.example.com --tls --device MYDSP01 --code-page 297 --keyboard FAB --wide
```

The default port is 23 for Telnet and 992 for direct TLS. Set `--port` to use another port.
The default EBCDIC code page is 37. The default keyboard type is `USB`.
The keyboard type and code page must match the configuration of the IBM i virtual device.
Use `--help` to see all options.

On Linux, use the equivalent project paths `src/Terminal5250` and `tests/Terminal5250.Tests` in these commands.

## Native AOT

Publish on the target operating system with its native compiler toolchain installed.
Windows requires Visual Studio C++ build tools and a Windows SDK.
Linux requires the .NET Native AOT prerequisites for the distribution, including a native compiler and zlib development files.

```powershell
dotnet publish src\Terminal5250 -c Release -r win-x64
dotnet publish src\Terminal5250 -c Release -r linux-x64
```

The second command runs on Linux, using `src/Terminal5250` as the project path.
Cross-compiling Windows to Linux or Linux to Windows is not supported by Native AOT.
The output is in the project's `bin\Release\net10.0\<rid>\publish` directory.
No .NET runtime installation is required on the destination machine.
Native operating-system dependencies, including the platform TLS implementation, are still required.
For a framework-dependent build, pass `-p:PublishAot=false`.

## Connections and TLS

The client connects directly to the IBM i Telnet service, including services used by IBM i Access Client Solutions (ACS).
ACS is a separate client, not a server or transport. The application does not launch ACS or read ACS session files.

`--tls` wraps the entire connection in TLS before Telnet negotiation. Certificate chain and host-name validation are mandatory.
Install a private certificate authority in the operating-system trust store when needed.
There is no certificate-validation bypass. Obsolete SSL protocols and Telnet STARTTLS are not implemented.
Plain Telnet sends the session, including sign-on input, without encryption.
No passwords are accepted on the command line, stored in settings, or written to trace files.
Sign on through the host's normal screen. Automatic sign-on and password substitutes are not implemented.

## Keyboard and display

Use an interactive console with at least 80 columns and 25 rows.
For `--wide`, allow 132 columns and 28 rows. A status line sits below the host display.
When the window is too small, resize it to resume input. Ctrl+Q remains available.

* Enter sends Enter.
* F1 through F12 send the corresponding function keys.
* Shift+F1 through Shift+F12 send F13 through F24.
* Tab and Shift+Tab select the next and previous input fields.
* Arrow keys move the cursor. Home selects the current field's start.
* End selects the end of the current field's content.
* Insert toggles insertion. Delete and Backspace remove characters.
* Ctrl+End erases the rest of the current field.
* Ctrl+Enter sends Field Exit. Keypad plus and minus perform Field+ and Field-.
* PageUp and PageDown send Roll Down and Roll Up.
* Alt+H sends Help, including the host error Help signal.
* Ctrl+A sends Attention. Ctrl+S sends System Request.
* Escape resets an input error. Ctrl+Q disconnects. Ctrl+C cancels the session.

Some terminal hosts intercept function keys, Shift+function keys, or Ctrl+S.
Configure the terminal host to pass these keys through. On Unix, disable software flow control if it captures Ctrl+S.

Color, reverse image, and non-display attributes are rendered using `System.Console`.
Underline, blinking, and column separator presentation are not currently rendered.
Protected fields cannot be edited. Non-display fields remain blank while retaining their input data for submission.

## Protocol scope

Implemented protocol facilities include the following.

* Telnet BINARY, EOR, terminal type, suppress go-ahead, IAC escaping, and bounded record framing.
* NEW-ENVIRON negotiation for device name, keyboard type, and code page.
* RFC 1205 GDS headers, cancel-invite acknowledgment, message indicator, Attention, and System Request flags.
* Clear Unit, Clear Unit Alternate, Clear Format Table, Write to Display, Roll, and Write Error Code.
* Read Input Fields, Read MDT Fields, alternate MDT reads, immediate reads, and Read Screen.
* Save and Restore Screen with an opaque client-specific representation.
* Device query response, 24 by 80 and 27 by 132 modes.
* SBA, SF, SOH, IC, MC, Repeat to Address, and Transparent Data orders.
* Modified field tracking, function-key masks, monocase, basic numeric validation, and mandatory entry checks.
* Basic field exit, right adjustment, mandatory fill, auto enter, and signed numeric submission.

Unsupported commands and field control words terminate the session with an explicit error.
Advanced field validation has not been certified against physical 5250 behavior.
Resequencing, self-check fields, DBCS, graphics, printer sessions, macros, mouse input, and ACS profile import are not implemented.
IBM startup response records are recognized. Device-name collision retry and automatic sign-on are not implemented.
The screen buffer and save records contain entered field data, including passwords, for as long as the protocol needs it.

## Verification

Run the dependency-free protocol test executable.

```powershell
dotnet run --project tests\Terminal5250.Tests -p:PublishAot=false
```

The tests use synthetic public-protocol fixtures and loopback servers.
They exercise fields, keyboard editing, masks, reads, malformed records, screen save/restore, wide mode, negotiation, and Telnet over TLS.
A self-signed certificate is accepted only by the test client inside the loopback fixture.
The production client always validates server certificates.
CI builds, tests, and publishes Native AOT on Windows and Linux.

Before relying on this terminal, test sign-on, command entry, password fields, function keys, subfiles, and System Request against your host.
Native AOT publishing, native executable smoke tests, and the protocol suite have been verified on Windows x64 and Ubuntu x64.
The tests currently perform 61 checks, including rejection of untrusted TLS certificates and 1000 malformed screen fixtures.

## Protocol references

* [RFC 1205, 5250 Telnet interface](https://www.rfc-editor.org/rfc/rfc1205)
* [RFC 4777, IBM's iSeries Telnet Enhancements](https://www.rfc-editor.org/rfc/rfc4777)
* [IBM 5250 Functions Reference, SA21-9247-6](https://bitsavers.trailing-edge.com/pdf/ibm/5250_5251/SA21-9247-6_IBM_5250_Information_Display_System_Functions_Reference_Manual_198703.pdf)
* [IBM 5250 Data Stream Details](https://www.ibm.com/docs/en/i/7.4.0?topic=ssw_ibm_i_74%2Fapis%2Fdsm1f.html)
