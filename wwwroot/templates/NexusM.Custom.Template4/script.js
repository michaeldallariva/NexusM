/* ------------------------------------------------------------------
   NexusM Custom Template 4 - "YablakoTV"
   Inspired by Jellyfin's AppleTV theme — topbar navigation.
   Single-row topbar: Logo — Nav links — [search + topbar-actions]
   ------------------------------------------------------------------ */
(function () {
  'use strict';

  /* Map data-page → i18n key so nav labels respect the active language */
  var PAGE_I18N = {
    home:        'nav.home',
    movies:      'nav.movies',
    tv:          'nav.tvShows',
    music:       'nav.music',
    musicvideos: 'nav.musicVideos',
    radio:       'nav.radio',
    internettv:  'nav.internetTv',
    podcasts:    'nav.podcasts',
    pictures:    'nav.pictures',
    ebooks:      'nav.ebooks',
    audiobooks:  'nav.audioBooks',
    anime:       'nav.anime',
    actors:      'nav.actors',
    favourites:  'nav.favourites',
    playlists:   'nav.playlists',
    mostplayed:  'nav.mostPlayed',
    watchlist:   'nav.watchlist',
    bestrated:   'nav.bestRated',
    analysis:    'nav.analysis',
    insights:    'nav.insights',
    settings:    'nav.settings',
    rescan:      'nav.rescanFolders'
  };

  /* Groups shown in the 9-dot more-menu panel */
  var MORE_GROUPS = [
    {
      labelKey: 'nav.collections',
      fallback: 'Collections',
      items: [
        { page: 'favourites', icon: 'icon-heart'      },
        { page: 'watchlist',  icon: 'icon-bookmark'   },
        { page: 'playlists',  icon: 'icon-list'       },
        { page: 'mostplayed', icon: 'icon-trending'   },
        { page: 'bestrated',  icon: 'icon-star'       }
      ]
    },
    {
      labelKey: 'nav.admin',
      fallback: 'Admin',
      items: [
        { page: 'analysis', icon: 'icon-bar-chart' },
        { page: 'insights', icon: 'icon-trending'  }
      ]
    }
  ];

  var SECONDARY = {
    favourites:1, playlists:1, watchlist:1, mostplayed:1, bestrated:1,
    analysis:1, insights:1, settings:1, rescan:1
  };
  /* Always append a Settings link at the very end of the nav */
  var APPEND_SETTINGS = true;

  function tl(page) {
    var key = PAGE_I18N[page];
    if (key && typeof App !== 'undefined' && App.t) return App.t(key);
    return null;
  }

  function svgUse(id) {
    return '<svg width="14" height="14" viewBox="0 0 24 24" fill="none"'
      + ' stroke="currentColor" stroke-width="1.8"'
      + ' stroke-linecap="round" stroke-linejoin="round">'
      + '<use href="#' + id + '"/></svg>';
  }

  function buildPearNav() {
    var topbar = document.querySelector('header.topbar');
    if (!topbar || document.getElementById('peartv-bar')) return;

    /* -- Outer bar ------------------------------------------------ */
    var bar = document.createElement('div');
    bar.id = 'peartv-bar';

    /* -- Logo ----------------------------------------------------- */
    var logo = document.createElement('a');
    logo.id = 'peartv-logo';
    logo.href = '#';
    logo.innerHTML = 'Nexus<span class="ptv-dot">M</span>';
    logo.onclick = function (e) { e.preventDefault(); doNavigate('home'); };

    /* -- Nav strip ------------------------------------------------ */
    var nav = document.createElement('nav');
    nav.id = 'peartv-nav';

    document.querySelectorAll('.sidebar-nav li a[data-page]').forEach(function (link) {
      var page = link.getAttribute('data-page');
      if (!page) return;
      var labelEl = link.querySelector('.nav-label');
      if (!labelEl || !labelEl.textContent.trim()) return;
      /* Prefer translated label via App.t; fall back to sidebar DOM text */
      var label = tl(page) || labelEl.textContent.trim();
      var iconUse = link.querySelector('.nav-icon svg use');
      var iconId  = iconUse ? (iconUse.getAttribute('href') || '').replace('#', '') : '';
      /* Skip secondary/admin pages to keep topbar uncluttered */
      if (SECONDARY[page]) return;
      var a = document.createElement('a');
      a.href = '#';
      a.setAttribute('data-page', page);
      a.setAttribute('data-peartv-link', '1');
      a.innerHTML = (iconId ? svgUse(iconId) : '') + '<span>' + label + '</span>';
      a.onclick = function (e) { e.preventDefault(); doNavigate(page); };
      nav.appendChild(a);
    });

    /* -- Settings link pinned at end ----------------------------- */
    if (APPEND_SETTINGS) {
      var settingsLink = document.querySelector('.sidebar-nav li a[data-page="settings"]');
      if (settingsLink) {
        var iconUse2 = settingsLink.querySelector('.nav-icon svg use');
        var iconId2  = iconUse2 ? (iconUse2.getAttribute('href') || '').replace('#', '') : '';
        var label2   = tl('settings') || (settingsLink.querySelector('.nav-label') || {}).textContent || 'Settings';
        var sep = document.createElement('span');
        sep.setAttribute('aria-hidden', 'true');
        sep.style.cssText = 'width:1px;height:18px;background:rgba(0,0,0,0.15);margin:0 4px;flex-shrink:0;align-self:center';
        var sa = document.createElement('a');
        sa.href = '#';
        sa.setAttribute('data-page', 'settings');
        sa.setAttribute('data-peartv-link', '1');
        sa.innerHTML = (iconId2 ? svgUse(iconId2) : '') + '<span>' + label2 + '</span>';
        sa.onclick = function (e) { e.preventDefault(); doNavigate('settings'); };
        nav.appendChild(sep);
        nav.appendChild(sa);
      }
    }

    /* -- Right slot: existing search + topbar-actions ------------ */
    var right = document.createElement('div');
    right.id = 'peartv-right';

    /* SAFE: move all existing topbar children into a hidden holder so
       #sidebar-expand-btn and other elements app.js needs stay in the DOM */
    var holder = document.createElement('div');
    holder.id = 'ptv-orig-holder';
    holder.style.cssText = 'display:none!important;position:absolute;pointer-events:none';
    while (topbar.firstChild) {
      holder.appendChild(topbar.firstChild);
    }

    /* -- 9-dot more-menu button (left of search, matching Amazing! layout) -- */
    var moreBtn = document.createElement('button');
    moreBtn.id = 'ptv-more-btn';
    moreBtn.className = 'topbar-btn';
    moreBtn.title = 'More';
    moreBtn.innerHTML =
      '<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" style="display:block">' +
      '<rect x="3" y="3" width="4" height="4" rx="0.8"/>' +
      '<rect x="10" y="3" width="4" height="4" rx="0.8"/>' +
      '<rect x="17" y="3" width="4" height="4" rx="0.8"/>' +
      '<rect x="3" y="10" width="4" height="4" rx="0.8"/>' +
      '<rect x="10" y="10" width="4" height="4" rx="0.8"/>' +
      '<rect x="17" y="10" width="4" height="4" rx="0.8"/>' +
      '<rect x="3" y="17" width="4" height="4" rx="0.8"/>' +
      '<rect x="10" y="17" width="4" height="4" rx="0.8"/>' +
      '<rect x="17" y="17" width="4" height="4" rx="0.8"/>' +
      '</svg>';

    var morePanel = document.createElement('div');
    morePanel.id = 'ptv-more-panel';
    morePanel.style.display = 'none';
    document.body.appendChild(morePanel);

    moreBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      var open = morePanel.style.display !== 'none';
      if (open) {
        morePanel.style.display = 'none';
      } else {
        var r = moreBtn.getBoundingClientRect();
        morePanel.style.top   = (r.bottom + 8) + 'px';
        morePanel.style.right = (window.innerWidth - r.right) + 'px';
        morePanel.style.left  = 'auto';
        morePanel.style.display = 'block';
        buildMorePanel(morePanel);
      }
    });
    document.addEventListener('click', function () {
      if (morePanel) morePanel.style.display = 'none';
    });
    morePanel.addEventListener('click', function (e) { e.stopPropagation(); });

    /* Order: [9-dot] [search] [actions] */
    right.appendChild(moreBtn);
    var sb = holder.querySelector('.search-box');
    var ta = holder.querySelector('.topbar-actions');
    if (sb) right.appendChild(sb);
    if (ta) right.appendChild(ta);

    /* Collapsed search — icon-only by default, expands on click */
    if (sb) {
      var searchIcon = sb.querySelector('.search-icon');
      var searchInp  = sb.querySelector('input');
      if (searchIcon) {
        searchIcon.addEventListener('click', function (e) {
          e.stopPropagation();
          var isOpen = sb.classList.toggle('ptv-search-open');
          if (isOpen && searchInp) searchInp.focus();
        });
      }
      if (searchInp) {
        searchInp.addEventListener('blur', function () {
          if (!searchInp.value) sb.classList.remove('ptv-search-open');
        });
      }
      sb.addEventListener('click', function (e) { e.stopPropagation(); });
      document.addEventListener('click', function () {
        if (sb && !sb.querySelector('input').value) sb.classList.remove('ptv-search-open');
      });
    }

    bar.appendChild(logo);
    bar.appendChild(nav);
    bar.appendChild(right);
    topbar.appendChild(bar);
    topbar.appendChild(holder);
  }

  function buildMorePanel(panel) {
    var html = '';
    MORE_GROUPS.forEach(function (group) {
      var groupLabel = (typeof App !== 'undefined' && App.t)
        ? App.t(group.labelKey) || group.fallback
        : group.fallback;

      /* Filter items hidden by menu-visibility settings */
      var visibleItems = group.items.filter(function (item) {
        var sidebarLink = document.querySelector('.sidebar-nav li a[data-page="' + item.page + '"]');
        var li = sidebarLink ? sidebarLink.closest('li') : null;
        return !li || li.style.display !== 'none';
      });
      if (visibleItems.length === 0) return;

      html += '<div class="ptv-more-group-label">' + groupLabel + '</div>';
      visibleItems.forEach(function (item) {
        var label = tl(item.page) || item.page;
        html += '<button class="ptv-more-item" data-page="' + item.page + '">' +
          svgUse(item.icon) +
          '<span>' + label + '</span>' +
          '</button>';
      });
    });

    panel.innerHTML = html;

    panel.querySelectorAll('.ptv-more-item').forEach(function (btn) {
      btn.addEventListener('click', function () {
        panel.style.display = 'none';
        doNavigate(btn.getAttribute('data-page'));
      });
    });
  }

  function doNavigate(page) {
    if (typeof App !== 'undefined' && App.navigate) App.navigate(page);
  }

  function syncActive(page) {
    document.querySelectorAll('a[data-peartv-link]').forEach(function (a) {
      a.classList.toggle('active', a.getAttribute('data-page') === page);
    });
  }

  /* Mirror sidebar li visibility onto topbar nav links */
  function syncVisibility() {
    document.querySelectorAll('a[data-peartv-link]').forEach(function (a) {
      var page = a.getAttribute('data-page');
      var sidebarLink = document.querySelector('.sidebar-nav li a[data-page="' + page + '"]');
      if (sidebarLink) {
        var li = sidebarLink.closest('li');
        a.style.display = (li && li.style.display === 'none') ? 'none' : '';
      }
    });
    /* Keep the separator before Settings in sync with Settings visibility */
    var settingsLink = document.querySelector('a[data-peartv-link][data-page="settings"]');
    if (settingsLink) {
      var sep = settingsLink.previousElementSibling;
      if (sep && sep.tagName === 'SPAN') sep.style.display = settingsLink.style.display;
    }
  }

  function patchNav() {
    if (typeof App === 'undefined' || !App.navigate || App._peartvPatched) return;
    App._peartvPatched = true;

    /* Patch navigate for active-link highlight */
    var _origNav = App.navigate.bind(App);
    App.navigate = function (page) {
      syncActive(page);
      return _origNav(page);
    };
    if (App._currentSection) syncActive(App._currentSection);

    /* Patch applyMenuVisibility so topbar hides/shows items whenever settings change */
    if (!App._peartvMenuPatched) {
      App._peartvMenuPatched = true;
      var _origAMV = App.applyMenuVisibility.bind(App);
      App.applyMenuVisibility = async function () {
        var result = await _origAMV();
        syncVisibility();
        return result;
      };
    }
  }

  /* -- Init: wait for App.t before building so labels are translated */
  var tick = 0;
  var iv = setInterval(function () {
    if (typeof App !== 'undefined' && App.t && App.navigate) {
      buildPearNav();
      syncVisibility(); /* sidebar visibility already set by init — mirror it now */
      patchNav();
      clearInterval(iv);
    }
    if (++tick > 80) {
      buildPearNav();
      syncVisibility();
      clearInterval(iv);
    }
  }, 150);

})();
