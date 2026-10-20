Build with the local SDK after sourcing `scripts/linux/env.sh`. This executable
uses synthetic screenshots and the real OverlayHostLoop. It refuses to connect
unless WAYLAND_DISPLAY starts with sx-. Run only with an explicitly authorized
nested compositor; never change the user's real display socket name to bypass the guard.
It is not run by the headless test script.
