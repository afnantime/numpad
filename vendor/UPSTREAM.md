# VirtualDesktop upstream

Repository: https://github.com/MScholtes/VirtualDesktop

Pinned revision: `a725cbd3cdb9e977678eeaf034a7cc96d2e74bc6`

File: `VirtualDesktop.cs`, Windows 10 1809–22H2, version 1.21.

SHA256: `CF929977462E4C53C3F211E743F548BDB41AE20E80166E314EB8A7A68BCD05A7`

The source is preserved unchanged and compiled into a separate library.
The upstream CLI entrypoint is not executed. See LICENSE for the MIT license.
Desktop enumeration, creation and switching rely on internal Windows COM
interfaces; compatibility must be verified on the target Windows build.
