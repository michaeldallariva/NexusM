/* ------------------------------------------------------------------
   NexusM Custom Template 1 - "Horizon"  |  script.js
   Two-row horizontal nav - SAFE version (never nukes existing DOM).
   Row 1 -> Logo - Primary nav - [existing search + topbar-actions]
   Row 2 -> Collections - | - Admin (Analysis, Settings...)
   ------------------------------------------------------------------ */
(function () {
  'use strict';

  var SECONDARY = {
    favourites:1, playlists:1, watchlist:1, mostplayed:1, bestrated:1,
    analysis:1, insights:1, settings:1, rescan:1
  };

  function svgUse(id) {
    return '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><use href="#' + id + '"/></svg>';
  }

  function buildHorizonNav() {
    var topbar = document.querySelector('header.topbar');
    if (!topbar || document.getElementById('horizon-row1')) return;

    /* -- Build row containers ------------------------------------ */
    var row1 = document.createElement('div');
    row1.id = 'horizon-row1';

    var row2 = document.createElement('div');
    row2.id = 'horizon-row2';

    /* -- Nav strips ---------------------------------------------- */
    var nav1 = document.createElement('nav');
    nav1.id = 'horizon-nav1';

    var nav2 = document.createElement('nav');
    nav2.id = 'horizon-nav2';

    /* -- Collect sidebar links into primary / secondary ---------- */
    document.querySelectorAll('.sidebar-nav li a[data-page]').forEach(function (link) {
      var page = link.getAttribute('data-page');
      if (!page) return;
      var labelEl = link.querySelector('.nav-label');
      if (!labelEl || !labelEl.textContent.trim()) return;
      var label = labelEl.textContent.trim();
      var iconUse = link.querySelector('.nav-icon svg use');
      var iconId  = iconUse ? (iconUse.getAttribute('href') || '').replace('#','') : '';
      var a = document.createElement('a');
      a.href = '#';
      a.setAttribute('data-page', page);
      a.setAttribute('data-horizon-link', '1');
      a.innerHTML = (iconId ? svgUse(iconId) : '') + '<span>' + label + '</span>';
      a.onclick = function (e) { e.preventDefault(); doNavigate(page); };
      (SECONDARY[page] ? nav2 : nav1).appendChild(a);
    });

    /* -- SAFE: move ALL existing topbar children into row1 ------- *
     * This keeps #sidebar-expand-btn, #page-title, .search-box,   *
     * .topbar-actions, etc. alive so app.js bindSidebar() works.  */
    while (topbar.firstChild) {
      row1.appendChild(topbar.firstChild);
    }

    /* -- Prepend logo + nav1 to the front of row1 ---------------- */
    var logo = document.createElement('a');
    logo.id = 'horizon-logo';
    logo.href = '#';
    logo.innerHTML = 'Nexus<span class="hl-dot">M</span>';
    logo.onclick = function (e) { e.preventDefault(); doNavigate('home'); };

    row1.insertBefore(nav1, row1.firstChild);
    row1.insertBefore(logo, nav1);

    /* -- Row 2: collections | admin ------------------------------ */
    row2.appendChild(nav2);

    /* -- Mount both rows ----------------------------------------- */
    topbar.appendChild(row1);
    topbar.appendChild(row2);
  }

  /* -- Safe navigate -------------------------------------------- */
  function doNavigate(page) {
    if (typeof App !== 'undefined' && App.navigate) App.navigate(page);
  }

  /* -- Sync active highlight across both rows ------------------- */
  function syncActive(page) {
    document.querySelectorAll('a[data-horizon-link]').forEach(function (a) {
      a.classList.toggle('active', a.getAttribute('data-page') === page);
    });
  }

  /* -- Patch App.navigate once it exists ------------------------ */
  function patchNav() {
    if (typeof App === 'undefined' || !App.navigate || App._horizonPatched) return;
    App._horizonPatched = true;
    var _orig = App.navigate.bind(App);
    App.navigate = function (page) {
      syncActive(page);
      return _orig(page);
    };
    if (App._currentSection) syncActive(App._currentSection);
  }

  /* -- Init ----------------------------------------------------- */
  buildHorizonNav();

  var tick = 0;
  var iv = setInterval(function () {
    if (typeof App !== 'undefined' && App.navigate) { patchNav(); clearInterval(iv); }
    if (++tick > 80) clearInterval(iv);
  }, 150);

})();
