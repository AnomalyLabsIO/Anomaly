# Reference publicizer

The server build needs member metadata that is not visible in the stock
`Assembly-CSharp.dll`. This build-only tool produces an independent public
compile reference from the operator-supplied stock game assembly. The generated
DLL is written below `obj/`, is never packaged, and retains the stock assembly
identity so the plugin continues to bind to the game's own runtime assembly.

No third-party mod framework binaries are used as build inputs.
