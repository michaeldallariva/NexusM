/* ------------------------------------------------------------------
   NexusM Custom Template 5 - "Plux!"
   Plex-inspired dark theme — sidebar layout, minimal JS.
   Keeps the default sidebar nav intact. Patches App.navigate to
   keep the active-link highlight in sync with the Plex orange style.
   ------------------------------------------------------------------ */
(function () {
  'use strict';

  function patchNav() {
    if (typeof App === 'undefined' || !App.navigate || App._pluxPatched) return;
    App._pluxPatched = true;
    var _orig = App.navigate.bind(App);
    App.navigate = function (page) {
      return _orig(page);
    };
  }

  var tick = 0;
  var iv = setInterval(function () {
    if (typeof App !== 'undefined' && App.navigate) { patchNav(); clearInterval(iv); }
    if (++tick > 80) clearInterval(iv);
  }, 150);

})();
