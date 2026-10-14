# UI design and motion

The supplied Sylva reference informed the rounded dock controls, lit rims, pointer highlights, light typography, muted dark surfaces, and flowing band motif. Uh chose a lightweight native adaptation. This is an independent WPF implementation, not the original Sylva renderer or a byte-identical port of its GLSL/Three.js scene.

The complete supplied reference was preserved unchanged locally for comparison (SHA-256 `f0c2678e31efa39dac02b0b1611490f760244dc79057c04cd9d95fcff46c707e`). The original HTML, third-party website copy, images, fonts, and runtime assets are not redistributed with the app. There is no embedded browser, external font fetch, or web asset dependency.

`AmbientScene` draws native vector bands and points. Its dispatcher timer is capped at 20 Hz and runs only for visible scenes when motion is allowed. Timers stop on unload and hidden pages. The window policy also stops motion when inactive, minimized, reduced motion is enabled, Windows animations are disabled, or FiveM is detected while pause-during-game is enabled. FiveM process state is polled about every ten seconds. Button highlight and press effects are event-driven, with no per-button frame loop.

Appearance presets and custom colors update shared dynamic resources across all pages, graphs, buttons, and artwork. Colors accept six hex digits with an optional leading `#`. Display accents and primary-button text are contrast-adjusted. Color identification includes text and a swatch. Preferences are saved only after Save appearance is chosen. Native vectors scale with Windows DPI.
