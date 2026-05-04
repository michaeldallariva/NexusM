/* ------------------------------------------------------------------
   NexusM Custom Template 3 - "Jellyfish"
   Jellyfin-inspired dark theme — sidebar layout, minimal JS.
   No DOM restructuring: the default sidebar nav is kept intact.
   This script only syncs the active-nav highlight on navigation.
   ------------------------------------------------------------------ */
(function () {
  'use strict';

  /* Sync sidebar active item highlight when App.navigate is called */
  function patchNav() {
    if (typeof App === 'undefined' || !App.navigate || App._jellyfishPatched) return;
    App._jellyfishPatched = true;
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
