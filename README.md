# 5250net

An IBM i TN5250 terminal, written in C# as a standard .NET 10 console application.
Native AOT publishing is enabled.

## Run

```powershell
5250net --help
5250net -- myibmi.example.com
5250net -- myibmi.example.com --tls --device MYDSP01 --code-page 297 --keyboard FAB --wide
```

The console title is `5250 - HOST` during a connection, and the previous title is restored on exit.
Unix title restoration uses the terminal's title stack when supported.
The default port is 23 for Telnet and 992 for direct TLS. Set `--port` to use another port.
The default EBCDIC code page is 37. The default keyboard type is `USB`.
The keyboard type and code page must match the configuration of the IBM i virtual device.
Use `--help` to see all options.

## Connections and TLS

Connections to any hostname try the preferred DNS address first, then stagger further attempts by 250 milliseconds.
The other address family is tried next when available. The first successful connection is used.
TLS certificate validation and SNI continue to use the hostname from the command line.

The client connects directly to the IBM i Telnet service, including services used by IBM i Access Client Solutions (ACS).
ACS is a separate client, not a server or transport. The application does not launch ACS or read ACS session files.

`--tls` wraps the entire connection in TLS before Telnet negotiation, with certificate chain and host-name validation enabled by default.
Use `--tls-certificate FILE` to trust a public CA or self-signed server certificate in PEM or DER format.
This uses the supplied certificate as the trust anchor, keeps host-name and expiry checks, and skips revocation lookups.
Use `--tls-trust-all` to disable certificate and host-name validation while keeping TLS encryption.
These options require `--tls` and cannot be combined with each other.

```powershell
5250net localhost --tls --tls-certificate C:\Certificates\server.pem
5250net localhost --tls --tls-trust-all
```

Obsolete SSL protocols and Telnet STARTTLS are not implemented.
Plain Telnet sends the session, including sign-on input, without encryption.
No passwords are accepted on the command line, stored in settings, or written to trace files.
Sign on through the host's normal screen. Automatic sign-on and password substitutes are not implemented.

## HTML screen capture

Use `--capture-directory PATH` to save complete host screens as standalone UTF-8 HTML pages.

```powershell
5250net localhost --capture-directory C:\TerminalCaptures
5250net localhost --tls --tls-trust-all --capture-directory C:\TerminalCaptures
```

Each process has its own session identifier, its process ID.
Each session writes to a separate `<process ID>_<session-start UTC timestamp>` subdirectory of the capture directory.
The session directory contains `index.html` and all captured frame pages.
The index header includes the application version, operating system and version, OS and process architectures, .NET runtime, and local time zone.
Frame files are named `<process ID>_<six-digit screen number>_<UTC timestamp>.html`.
For example, `1234_000001_20261031T1200001234567Z.html` is the first saved screen of process 1234.

A page is saved after a complete host record updates the display and its visible contents or styling differ from the last saved screen.
Typing, cursor-only changes, duplicate reloads, and changes confined to non-display contents do not create pages.
Each page contains the full 80-column or 132-column screen with embedded colors, reverse image, and underlining.
Unicode characters, spaces, and row boundaries are preserved. No external styles, fonts, or scripts are required.
An HTML-only footer records the original argument tokens and connection metadata, including TLS status, validation mode, protocol, and cipher.
It also shows the process ID, frame number, capture time, remote endpoint, device, keyboard, and code page.
Previous and Next links use relative filenames, so the archive can be moved to another directory.
The first frame has no Previous link. The latest frame has no Next link.
Every frame also links back to the session index.
The index has a contrasting slate background, while embedded screens keep their black background.
The index has a session header with the connection details and command-line arguments, links to all frames, and lazy-loaded embedded screens.
Embedded screens hide their individual footers. Opening a frame's link shows its normal footer and navigation.
The index is updated atomically when a new frame is saved. Reload an open index page to see new entries.
Each new frame atomically updates the preceding file to add its Next link. Duplicate screens do not modify the navigation.

The pages contain visible screen contents. Non-display field values are omitted from the HTML source.
The directory is created if needed. A write failure reports an error and ends the connection through normal session cleanup.

Capture pages identify the startup screen, reloaded screens, and screens saved before leaving.
Before submitting an action or ending the session, the current screen is saved only if its visible content differs from the last capture.
Screen footers and index headings identify the submitted action, such as Enter or F3. Form values are not included in action labels.
Enter includes a numeric menu choice when exactly one modified editable field contains only digits.
A sole editable field containing digits also qualifies when its modified flag is clear.
This includes row selections on lists with multiple input fields.
Masked fields and transparent fields are excluded from numeric action labels.
Each screen shows its action as keyboard badges in the footer and beside its preview in the session index.
Numeric choices show separate badges, for example `2` followed by `Enter`.
Capture directories are created only when the first screen is written.

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
* Escape resets an input error. Ctrl+Q disconnects. Ctrl+C also disconnects.

Disconnect drains incoming data before disposing the socket, including when a pending record is malformed.
Exit briefly drains queued key events and restores the previous Ctrl+C input setting.
The console API does not expose physical modifier-key release state, and the application does not synthesize global key releases.

Some terminal hosts intercept function keys, Shift+function keys, or Ctrl+S.
Configure the terminal host to pass these keys through. On Unix, disable software flow control if it captures Ctrl+S.

Color, reverse image, underline, and non-display attributes are rendered in the console.
Windows rendering uses portable `System.Console` APIs, with underscores marking blank underlined fields.
Unix terminals use ANSI underlining, with the same underscore fallback when `TERM` is `dumb`.
No native console imports are used.
Blinking and column separator presentation are not currently rendered.
Protected fields cannot be edited. Non-display fields remain blank while retaining their input data for submission.

## Manual GitHub builds

The `build` workflow runs only when started manually from the Actions tab or the GitHub CLI.
It checks and publishes Windows x64 and Linux x64 Native AOT builds with .NET 10.
Build artifacts include the native executable and debug symbols.
Downloadable packages include the executable, README, and license, plus the PNG icon when available.
Windows packages are ZIP files. Linux packages are tar.gz files to preserve the executable permission.
The Linux build uses Ubuntu 24.04 as its native library baseline.

```powershell
gh workflow run build.yml
gh workflow run build.yml -f ref=main
gh workflow run build.yml -f release=true
```

The optional `release` input defaults to false. When enabled, it creates a release tagged from `Version` in the project file.
Both builds and the release use the same resolved commit. An existing release or a tag pointing at another commit stops publication.

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

## Protocol references

* [RFC 1205, 5250 Telnet interface](https://www.rfc-editor.org/rfc/rfc1205)
* [RFC 4777, IBM's iSeries Telnet Enhancements](https://www.rfc-editor.org/rfc/rfc4777)
* [IBM 5250 Functions Reference, SA21-9247-6](https://bitsavers.trailing-edge.com/pdf/ibm/5250_5251/SA21-9247-6_IBM_5250_Information_Display_System_Functions_Reference_Manual_198703.pdf)
* [IBM 5250 Data Stream Details](https://www.ibm.com/docs/en/i/7.4.0?topic=ssw_ibm_i_74%2Fapis%2Fdsm1f.html)
