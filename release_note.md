# TabCloser v1.1

This release adds permanent tray-icon hiding while keeping TabCloser running in the background.

## What's New

- Added **Hide tray icon** to the notification-area menu.
- The hidden state is remembered after signing out or restarting Windows.
- Starting TabCloser with Windows now keeps the icon hidden when requested.
- Launching `TabCloser.exe` manually restores the icon without opening a duplicate instance.

## Fixed

- Fixed the tray icon reappearing after a system restart.
- Existing TabCloser startup registrations are updated automatically for the new behavior.

## Download and Update

Download `TabCloser.exe` from the release assets. If upgrading from v1.0, exit the running TabCloser app before replacing the executable, then launch the new version once.

Windows may display a security warning because the executable is currently unsigned.
