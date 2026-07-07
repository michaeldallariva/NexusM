/* ------------------------------------------------------------------
   NexusM Custom Template 6 — "Netflux"
   Netflix-inspired layout: hero banner + horizontal shelf rows
   with animated hover card pop-ups.
   ------------------------------------------------------------------ */
(function () {
  'use strict';

  /* ── State ─────────────────────────────────────────────────────── */
  var _heroCycleTimer  = null;
  var _heroCycleItems  = [];
  var _heroCycleIdx    = 0;
  var _renderGen       = 0;
  var _cardData        = {};   /* id → video object, for popup */
  var _popupTimer      = null;
  var _popupActiveCard = null;
  var _p1Done          = false;
  var _p2Done          = false;

  /* ── Hero video preview state ────────────────────────────────── */
  var _heroVidTimer  = null;
  var _heroMuted     = true;

  /* ── Genre view state ────────────────────────────────────────── */
  var _nfCurrentGenre = null;

  /* ── Helpers ────────────────────────────────────────────────────── */
  function esc(s) {
    if (!s) return '';
    return String(s)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;')
      .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function _shuffleArr(arr) {
    var a = arr.slice();
    for (var i = a.length - 1; i > 0; i--) {
      var j = Math.floor(Math.random() * (i + 1));
      var t = a[i]; a[i] = a[j]; a[j] = t;
    }
    return a;
  }

  /* Build hero pool: up to 10 movies + 10 TV shows (one per series) + 10 docs, shuffled, max 30. */
  function _buildHeroPool(movies, tvShows, docs) {
    var moviesPool = _shuffleArr(movies.filter(function (v) { return v.backdropPath; }).slice(0, 20));

    // Deduplicate TV: one random episode per series
    var tvWithBackdrop = tvShows.filter(function (v) { return v.backdropPath; });
    var tvByShow = {};
    tvWithBackdrop.forEach(function (v) {
      var key = (v.seriesName || v.title || '').toLowerCase();
      if (!tvByShow[key]) tvByShow[key] = [];
      tvByShow[key].push(v);
    });
    var tvOnePer = [];
    Object.keys(tvByShow).forEach(function (k) {
      var eps = tvByShow[k];
      tvOnePer.push(eps[Math.floor(Math.random() * eps.length)]);
    });
    var tvPool   = _shuffleArr(tvOnePer.slice(0, 20));
    var docsPool = _shuffleArr((docs || []).filter(function (v) { return v.backdropPath; }).slice(0, 20));

    // Take up to 10 from each type for a balanced mix, then shuffle the combined pool
    var pool = _shuffleArr(
      moviesPool.slice(0, 10).concat(tvPool.slice(0, 10)).concat(docsPool.slice(0, 10))
    );
    if (!pool.length) {
      var fallback = movies[0] || tvShows[0] || (docs && docs[0]);
      if (fallback) pool = [fallback];
    }
    return pool;
  }

  /* ════════════════════════════════════════════════
     1. NAV BAR BUILD
  ════════════════════════════════════════════════ */
  function buildNav() {
    var topbar = document.querySelector('header.topbar');
    if (!topbar || document.getElementById('nf-bar')) return;

    var bar = document.createElement('div');
    bar.id = 'nf-bar';

    /* Logo */
    var logo = document.createElement('a');
    logo.id = 'nf-logo';
    logo.href = '#';
    logo.textContent = 'NEXUSM';
    logo.addEventListener('click', function (e) { e.preventDefault(); nfNav('home'); });
    bar.appendChild(logo);

    /* Tab strip — populated in phase 2 */
    var tabs = document.createElement('nav');
    tabs.id = 'nf-tabs';
    bar.appendChild(tabs);

    /* Right side */
    var right = document.createElement('div');
    right.id = 'nf-right';

    /* Move all original topbar children into a hidden holder so
       NexusM's bound event handlers (search, scan, etc.) stay intact */
    var holder = document.createElement('div');
    holder.id = 'nf-orig-holder';
    holder.style.cssText = 'display:none!important;position:absolute;pointer-events:none';
    while (topbar.firstChild) { holder.appendChild(topbar.firstChild); }

    /* Search — standalone (avoids app.css conflicts with moved .search-box) */
    var srchWrap = document.createElement('div');
    srchWrap.id = 'nf-search-wrap';
    srchWrap.innerHTML =
      '<button id="nf-search-btn" aria-label="Search">' +
        '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-search"/></svg>' +
      '</button>' +
      '<input id="nf-search-input" type="text" placeholder="Search…" autocomplete="off">';
    right.appendChild(srchWrap);

    var _sBtn = srchWrap.querySelector('button');
    var _sInp = srchWrap.querySelector('input');
    var _sTmr;
    _sBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      srchWrap.classList.toggle('nf-search-open');
      if (srchWrap.classList.contains('nf-search-open')) _sInp.focus();
    });
    _sInp.addEventListener('input', function () {
      clearTimeout(_sTmr);
      var q = _sInp.value.trim();
      if (q.length < 2) return;
      _sTmr = setTimeout(function () {
        if (typeof App !== 'undefined' && App.performSearch) App.performSearch(q);
      }, 300);
    });
    _sInp.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') {
        var q = _sInp.value.trim();
        if (q.length >= 2 && typeof App !== 'undefined' && App.performSearch) App.performSearch(q);
      }
      if (e.key === 'Escape') { _sInp.value = ''; srchWrap.classList.remove('nf-search-open'); }
    });
    _sInp.addEventListener('blur', function () {
      setTimeout(function () { if (!_sInp.value) srchWrap.classList.remove('nf-search-open'); }, 150);
    });
    srchWrap.addEventListener('click', function (e) { e.stopPropagation(); });
    document.addEventListener('click', function () {
      if (!_sInp.value) srchWrap.classList.remove('nf-search-open');
    });

    /* Account wrap — filled in phase 2 */
    var acWrap = document.createElement('div');
    acWrap.id = 'nf-account-wrap';
    right.appendChild(acWrap);

    bar.appendChild(right);
    topbar.appendChild(bar);
    topbar.appendChild(holder);

    /* Scroll: transparent → solid */
    var mc = document.getElementById('main-content');
    if (mc) {
      mc.addEventListener('scroll', function () {
        topbar.classList.toggle('nf-scrolled', mc.scrollTop > 8);
      }, { passive: true });
    }
  }

  /* ════════════════════════════════════════════════
     2. TABS (phase 2 — needs _initCfg)
  ════════════════════════════════════════════════ */
  function buildTabs() {
    var el = document.getElementById('nf-tabs');
    if (!el || el._nfBuilt) return;
    el._nfBuilt = true;

    var cfg       = typeof App !== 'undefined' ? App._initCfg : null;
    var showMV    = cfg && cfg.showMusicVideos;
    var showAnime = cfg && cfg.showAnime;

    var pages = [
      { page: 'home',          label: 'Home'          },
      { page: 'tvshows',       label: 'Shows'         },
      { page: 'movies',        label: 'Movies'        },
      { page: 'documentary',   label: 'Documentaries' },
      { page: 'nfnew',         label: 'New & Popular' },
      { page: 'watchlist',     label: 'Watchlist'     },
      { page: 'music',         label: 'Music'         },
    ];
    if (showMV)    pages.push({ page: 'musicvideos', label: 'Music Videos' });
    if (showAnime) pages.push({ page: 'anime',       label: 'Animes'      });

    pages.forEach(function (item) {
      var a = document.createElement('a');
      a.href = '#';
      a.className = 'nf-tab';
      a.setAttribute('data-nf-page', item.page);
      a.textContent = item.label;
      a.addEventListener('click', function (e) { e.preventDefault(); nfNav(item.page); });
      el.appendChild(a);
    });

    /* Browse by Genre tab — menu is a body-level fixed element to escape overflow:auto clipping */
    var navItems =
      '<a class="nf-genre-nav-item" href="#" onclick="event.preventDefault();window._nfGenreClose();window._nfNav(\'movies\')">Movies</a>' +
      '<a class="nf-genre-nav-item" href="#" onclick="event.preventDefault();window._nfGenreClose();window._nfNav(\'tvshows\')">TV Shows</a>' +
      '<a class="nf-genre-nav-item" href="#" onclick="event.preventDefault();window._nfGenreClose();window._nfNav(\'documentary\')">Documentaries</a>' +
      '<a class="nf-genre-nav-item" href="#" onclick="event.preventDefault();window._nfGenreClose();window._nfNav(\'nfnew\')">New &amp; Popular</a>' +
      '<a class="nf-genre-nav-item" href="#" onclick="event.preventDefault();window._nfGenreClose();window._nfNav(\'watchlist\')">Watchlist</a>';
    if (showMV)    navItems += '<a class="nf-genre-nav-item" href="#" onclick="event.preventDefault();window._nfGenreClose();window._nfNav(\'musicvideos\')">Music Videos</a>';
    if (showAnime) navItems += '<a class="nf-genre-nav-item" href="#" onclick="event.preventDefault();window._nfGenreClose();window._nfNav(\'anime\')">Animes</a>';

    /* Tab trigger */
    var gTabWrap = document.createElement('div');
    gTabWrap.className = 'nf-tab nf-genre-tab-wrap';
    gTabWrap.id = 'nf-genre-tab-wrap';
    gTabWrap.innerHTML =
      'Browse by Genre' +
      '<svg class="nf-genre-caret" width="10" height="6" viewBox="0 0 10 6" fill="currentColor"><polygon points="0,0 10,0 5,6"/></svg>';
    el.appendChild(gTabWrap);

    /* Menu appended to body — avoids overflow:auto clipping inside #nf-tabs */
    var gMenu = document.createElement('div');
    gMenu.id = 'nf-genre-menu';
    gMenu.className = 'nf-genre-menu';
    gMenu.innerHTML =
      '<div class="nf-genre-col">' +
        '<div class="nf-genre-col-hd">GENRES</div>' +
        '<div class="nf-genre-grid" id="nf-genre-grid"><div class="nf-genre-loading">Loading…</div></div>' +
      '</div>' +
      '<div class="nf-genre-col">' +
        '<div class="nf-genre-col-hd">NAVIGATE</div>' +
        '<div class="nf-genre-nav-col">' + navItems + '</div>' +
      '</div>';
    document.body.appendChild(gMenu);

    var _genreLoaded = false;
    function _loadGenres() {
      if (_genreLoaded) return;
      _genreLoaded = true;
      var grid = document.getElementById('nf-genre-grid');
      if (!grid) return;
      App.api('videos/genres').then(function (data) {
        if (!data || !data.length) { grid.innerHTML = '<span class="nf-genre-loading">No genres found.</span>'; return; }
        var html = '';
        data.forEach(function (g) {
          var name = typeof g === 'string' ? g : g.name;
          if (!name) return;
          if (name.toLowerCase() === 'documentary') return; // shown as its own nav tab
          html += '<a class="nf-genre-item" href="#" onclick="event.preventDefault();window._nfGenreNav(\'' + name.replace(/'/g, "\\'") + '\')">' + name + '</a>';
        });
        grid.innerHTML = html;
      }).catch(function () { grid.innerHTML = '<span class="nf-genre-loading">Could not load genres.</span>'; });
    }

    window._nfGenreClose = function () {
      gMenu.classList.remove('nf-genre-open');
      gTabWrap.classList.remove('nf-genre-open');
    };
    window._nfGenreNav = function (g) {
      window._nfGenreClose();
      _nfCurrentGenre = g;
      nfNav('nfgenre');
    };

    gTabWrap.addEventListener('click', function (e) {
      e.stopPropagation();
      var opening = !gMenu.classList.contains('nf-genre-open');
      if (opening) {
        var rect = gTabWrap.getBoundingClientRect();
        gMenu.style.top  = (rect.bottom + 6) + 'px';
        gMenu.style.left = rect.left + 'px';
        gMenu.classList.add('nf-genre-open');
        gTabWrap.classList.add('nf-genre-open');
        _loadGenres();
      } else {
        window._nfGenreClose();
      }
    });
    gMenu.addEventListener('click', function (e) { e.stopPropagation(); });
    document.addEventListener('click', function () { window._nfGenreClose(); });
  }

  /* ════════════════════════════════════════════════
     3. ACCOUNT BUTTON (phase 2)
  ════════════════════════════════════════════════ */
  function buildAccount() {
    var wrap = document.getElementById('nf-account-wrap');
    if (!wrap || wrap._nfBuilt) return;
    wrap._nfBuilt = true;

    var username = typeof App !== 'undefined' ? (App.userName || 'Account') : 'Account';
    var isAdmin  = typeof App !== 'undefined' && App.userRole === 'admin';
    var picUrl   = '/api/auth/users/' + encodeURIComponent(username) +
                   '/profile-picture?t=' + Date.now();

    /* Button */
    var btn = document.createElement('button');
    btn.id = 'nf-account-btn';
    btn.setAttribute('aria-label', 'Account menu');

    /* Avatar */
    var av = document.createElement('div');
    av.className = 'nf-avatar';

    /* Fallback icon (always rendered; hidden by img overlay when pic loads) */
    var iconDiv = document.createElement('div');
    iconDiv.className = 'nf-avatar-icon';
    iconDiv.innerHTML =
      '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"' +
      ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="display:block">' +
      '<path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>' +
      '<circle cx="12" cy="7" r="4"/></svg>';
    av.appendChild(iconDiv);

    /* Profile picture — removes itself on 404 revealing the icon */
    var picImg = document.createElement('img');
    picImg.src = picUrl;
    picImg.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;object-fit:cover';
    picImg.addEventListener('error', function () { this.remove(); });
    av.appendChild(picImg);

    /* Caret */
    var caretSvg =
      '<svg class="nf-caret-svg" width="10" height="6" viewBox="0 0 10 6" fill="#fff">' +
      '<polygon points="0,0 10,0 5,6"/></svg>';

    btn.appendChild(av);
    btn.insertAdjacentHTML('beforeend', caretSvg);
    wrap.appendChild(btn);

    /* Dropdown */
    var menu = document.createElement('div');
    menu.id = 'nf-account-menu';
    menu.style.display = 'none';
    wrap.appendChild(menu);

    function refreshMenu() {
      var uname = typeof App !== 'undefined' ? (App.userDisplayName || App.userName || 'Account') : 'Account';
      var admin = typeof App !== 'undefined' && App.userRole === 'admin';
      var settingsRow = admin
        ? '<button class="nf-menu-item" id="nf-mi-settings">' +
          '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
          '<circle cx="12" cy="12" r="3"/>' +
          '<path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>' +
          'Settings</button>'
        : '';

      menu.innerHTML =
        '<div class="nf-menu-user">' +
        '<div class="nf-avatar" style="width:26px;height:26px;border-radius:3px;flex-shrink:0">' +
        '<div class="nf-avatar-icon"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="display:block"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg></div>' +
        '</div>' +
        '<span class="nf-menu-user-name">' + esc(uname) + '</span>' +
        '</div>' +
        settingsRow +
        '<button class="nf-menu-item nf-menu-danger" id="nf-mi-signout">' +
        '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
        '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/>' +
        '<polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>' +
        'Sign out</button>';

      var settBtn = document.getElementById('nf-mi-settings');
      if (settBtn) {
        settBtn.addEventListener('click', function (e) {
          e.stopPropagation(); closeMenu(); nfNav('settings');
        });
      }
      var soBtn = document.getElementById('nf-mi-signout');
      if (soBtn) {
        soBtn.addEventListener('click', function (e) {
          e.stopPropagation(); closeMenu();
          if (typeof App !== 'undefined' && App.logout) App.logout();
        });
      }
    }

    function openMenu()  { menu.style.display = 'block'; wrap.classList.add('nf-menu-open');    refreshMenu(); }
    function closeMenu() { menu.style.display = 'none';  wrap.classList.remove('nf-menu-open'); }

    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      menu.style.display !== 'none' ? closeMenu() : openMenu();
    });
    document.addEventListener('click', function () { closeMenu(); });
    menu.addEventListener('click', function (e) { e.stopPropagation(); });
  }

  /* ════════════════════════════════════════════════
     4. NAVIGATION
  ════════════════════════════════════════════════ */
  function nfNav(page) {
    syncActive(page);
    clearHeroCycle();
    hidePopup();
    if (page === 'nfnew') {
      if (typeof App !== 'undefined') App.currentPage = 'nfnew';
      var mc = document.getElementById('main-content');
      if (mc) mc.classList.remove('nf-detail-view');
      renderNewPopular(mc);
      return;
    }
    if (page === 'nfgenre') {
      if (typeof App !== 'undefined') App.currentPage = 'nfgenre';
      var mcg = document.getElementById('main-content');
      if (mcg) mcg.classList.remove('nf-detail-view');
      renderGenreView(_nfCurrentGenre, mcg);
      return;
    }
    if (typeof App !== 'undefined') App.navigate(page);
  }
  window._nfNav = nfNav;

  function syncActive(page) {
    document.querySelectorAll('.nf-tab').forEach(function (a) {
      a.classList.toggle('nf-tab-active', a.getAttribute('data-nf-page') === page);
    });
  }

  /* ════════════════════════════════════════════════
     5. RENDER ENGINE
  ════════════════════════════════════════════════ */

  /* ── Home ───────────────────────────────────────── */
  async function renderHome(el) {
    if (!el) el = document.getElementById('main-content');
    if (!el) return;
    var gen = ++_renderGen;
    el.innerHTML = '<div class="nf-loading">Loading…</div>';
    try {
      var cfg       = typeof App !== 'undefined' ? App._initCfg : null;
      var showAnime = cfg && cfg.showAnime;
      var showMV    = cfg && cfg.showMusicVideos;

      var reqs = [
        App.api('videos?mediaType=movie&sort=recent&limit=200&grouped=true'),
        App.api('videos?mediaType=tv&sort=recent&limit=150&grouped=true'),
        App.api('videos/continue-watching').catch(function () { return []; }),
        App.api('videos?mediaType=documentary&sort=recent&limit=100'),
      ];
      var animeIdx = reqs.length;
      if (showAnime) reqs.push(App.api('videos?mediaType=anime&sort=recent&limit=80&grouped=true'));
      var mvsIdx = reqs.length;
      if (showMV)    reqs.push(App.api('musicvideos?limit=40&sort=recent'));

      var res = await Promise.all(reqs);
      if (_renderGen !== gen) return;

      var movies    = extractVids(res[0]);
      var tvShows   = extractVids(res[1]);
      var cw        = Array.isArray(res[2]) ? res[2] : [];
      var docs      = extractVids(res[3]);
      var anime     = showAnime ? extractVids(res[animeIdx]) : [];
      var mvsData   = showMV   ? (res[mvsIdx] || {}) : null;

      cw = cw.filter(function (v) {
        return ['movie','tv','anime','documentary'].indexOf(v.mediaType || '') !== -1;
      });

      /* Hero pool: balanced mix of movies, TV shows, and documentaries — up to 30 */
      var heroPool = _buildHeroPool(movies, tvShows, docs);
      var heroItem = heroPool[0] || null;

      var html = heroItem ? buildHero(heroItem) : '';
      html += '<div class="nf-shelves">';
      if (cw.length        > 0) html += buildShelf('Continue Watching',       cw.slice(0, 20),         'mixed');
      if (movies.length    > 0) html += buildShelf('Recently Added Movies',   movies.slice(0, 24),     'movie');
      if (tvShows.length   > 0) html += buildShelf('Recently Added TV Shows', tvShows.slice(0, 24),    'tv');
      if (anime.length     > 0) html += buildShelf('Anime',                   anime.slice(0, 24),      'anime');
      /* Genre rows from movies */
      var byGenre = groupByGenre(movies);
      Object.keys(byGenre).slice(0, 5).forEach(function (g) {
        var vids = byGenre[g];
        if (vids.length < 2) return;
        html += buildShelf(g, vids.slice(0, 24), 'movie');
      });
      if (mvsData && mvsData.videos && mvsData.videos.length > 0) {
        html += buildShelf('Music Videos', mvsData.videos.slice(0, 24), 'mv');
      }
      html += '</div>';
      el.innerHTML = html;
      setupHeroCycle(heroPool);
      bindHoverPopup();
      setupArrows();
    } catch (err) {
      console.error('[Netflux] home error:', err);
      el.innerHTML = '<div class="nf-empty">Could not load content.</div>';
    }
  }

  /* ── Generic (movies / TV / anime) ─────────────── */
  async function renderMedia(mediaType, el) {
    if (!el) el = document.getElementById('main-content');
    if (!el) return;
    var gen = ++_renderGen;
    el.innerHTML = '<div class="nf-loading">Loading…</div>';
    try {
      var [recentData, genreData] = await Promise.all([
        App.api('videos?mediaType=' + encodeURIComponent(mediaType) + '&sort=recent&limit=300&grouped=true'),
        App.api('videos/genres?mediaType=' + encodeURIComponent(mediaType))
      ]);
      if (_renderGen !== gen) return;

      var all    = extractVids(recentData);
      var genres = extractGenres(genreData);

      var heroPool = all.filter(function (v) { return v.backdropPath; }).slice(0, 30);
      var heroItem = heroPool[0] || all[0] || null;

      var html = heroItem ? buildHero(heroItem) : '';
      html += '<div class="nf-shelves">';
      if (mediaType === 'documentary') {
        // All items here are already documentaries — one flat shelf is cleaner than
        // fragmenting by sub-genre (many docs have no genre tag at all).
        html += buildShelf('All Documentaries', all.slice(0, 100), mediaType);
      } else if (genres.length > 0) {
        var byGenre = groupByGenre(all);
        var rowCount = 0;
        genres.forEach(function (g) {
          var vids = byGenre[g] || [];
          if (vids.length < 1) return;
          html += buildShelf(g, vids.slice(0, 24), mediaType);
          rowCount++;
        });
        if (rowCount === 0) html += buildShelf('All', all.slice(0, 48), mediaType);
      } else {
        html += buildShelf('All', all.slice(0, 48), mediaType);
      }
      html += '</div>';
      el.innerHTML = html;
      setupHeroCycle(heroPool);
      bindHoverPopup();
      setupArrows();
    } catch (err) {
      el.innerHTML = '<div class="nf-empty">Could not load content.</div>';
    }
  }

  /* ── Genre view — all media matching one genre ───────────────── */
  async function renderGenreView(genre, el) {
    if (!el) el = document.getElementById('main-content');
    if (!el) return;
    var gen = ++_renderGen;
    el.innerHTML = '<div class="nf-loading">Loading…</div>';
    if (!genre) { el.innerHTML = '<div class="nf-empty">No genre selected.</div>'; return; }
    try {
      var [moviesData, tvData, docsData] = await Promise.all([
        App.api('videos?mediaType=movie&sort=recent&limit=400'),
        App.api('videos?mediaType=tv&sort=recent&limit=400&grouped=true'),
        App.api('videos?mediaType=documentary&sort=recent&limit=200')
      ]);
      if (_renderGen !== gen) return;

      var genreLower = genre.toLowerCase();
      function matchesGenre(v) {
        if (!v.genre) return false;
        return v.genre.split(',').some(function (g) { return g.trim().toLowerCase() === genreLower; });
      }

      var all = extractVids(moviesData).concat(extractVids(tvData)).concat(extractVids(docsData));
      var filtered = all.filter(matchesGenre);

      if (filtered.length === 0) {
        el.innerHTML = '<div class="nf-empty">No content found for &ldquo;' + esc(genre) + '&rdquo;.</div>';
        return;
      }

      filtered.sort(function (a, b) {
        return new Date(b.dateAdded || 0).getTime() - new Date(a.dateAdded || 0).getTime();
      });

      var heroPool = filtered.filter(function (v) { return v.backdropPath; });
      var heroItem = heroPool[0] || filtered[0] || null;

      var html = heroItem ? buildHero(heroItem) : '';
      html += '<div class="nf-shelves">';
      html += buildShelf(genre, filtered.slice(0, 100), 'mixed');
      html += '</div>';

      el.innerHTML = html;
      setupHeroCycle(heroPool.slice(0, 30));
      bindHoverPopup();
      setupArrows();
    } catch (err) {
      el.innerHTML = '<div class="nf-empty">Could not load content.</div>';
    }
  }

  /* ── New & Popular (last 30 days from local library) ─────────── */
  async function renderNewPopular(el) {
    if (!el) el = document.getElementById('main-content');
    if (!el) return;
    var gen    = ++_renderGen;
    el.innerHTML = '<div class="nf-loading">Loading…</div>';

    var cutoffMs = Date.now() - 30 * 24 * 60 * 60 * 1000;
    function filterRecent(items) {
      return (items || []).filter(function (v) {
        if (!v.dateAdded) return false;
        return new Date(v.dateAdded).getTime() >= cutoffMs;
      }).sort(function (a, b) {
        return new Date(b.dateAdded).getTime() - new Date(a.dateAdded).getTime();
      });
    }

    try {
      var cfg       = typeof App !== 'undefined' ? App._initCfg : null;
      var showAnime = cfg && cfg.showAnime;
      var showMV    = cfg && cfg.showMusicVideos;

      var reqs = [
        App.api('videos?mediaType=movie&sort=recent&limit=120&grouped=true'),
        App.api('videos?mediaType=tv&sort=recent&limit=120&grouped=true'),
        App.api('videos?mediaType=documentary&sort=recent&limit=80'),
      ];
      var animeIdx = reqs.length;
      if (showAnime) reqs.push(App.api('videos?mediaType=anime&sort=recent&limit=80&grouped=true'));
      var mvsIdx = reqs.length;
      if (showMV)    reqs.push(App.api('musicvideos?limit=80&sort=recent'));

      var res = await Promise.all(reqs);
      if (_renderGen !== gen) return;

      var movies  = filterRecent(extractVids(res[0]));
      var tvShows = filterRecent(extractVids(res[1]));
      var docs    = filterRecent(extractVids(res[2]));
      var anime   = showAnime ? filterRecent(extractVids(res[animeIdx]))          : [];
      var mvRaw   = showMV    ? ((res[mvsIdx] || {}).videos || [])                : [];
      var mvs     = filterRecent(mvRaw);

      var total   = movies.length + tvShows.length + docs.length + anime.length + mvs.length;

      /* Hero: balanced mix of movies, TV, and docs — no music videos, up to 30 */
      var heroPool = _shuffleArr(
        _shuffleArr(movies.filter(function (v) { return v.backdropPath; })).slice(0, 10)
          .concat(_shuffleArr(tvShows.filter(function (v) { return v.backdropPath; })).slice(0, 10))
          .concat(_shuffleArr(docs.filter(function (v) { return v.backdropPath; })).slice(0, 10))
      );
      var heroItem = heroPool[0] || null;

      var html = heroItem ? buildHero(heroItem) : buildNpBanner(total);
      html += '<div class="nf-shelves nf-np-shelves">';

      if (total === 0) {
        html += '<div class="nf-np-empty">' +
          '<div class="nf-np-empty-icon">&#128250;</div>' +
          '<div class="nf-np-empty-title">Nothing new in the last 30 days</div>' +
          '<div class="nf-np-empty-sub">New movies, shows, and music videos you add to your library will appear here.</div>' +
          '</div>';
      } else {
        if (movies.length  > 0) html += buildShelf('New Movies',       movies,  'movie');
        if (tvShows.length > 0) html += buildShelf('New TV Shows',     tvShows, 'tv');
        if (anime.length   > 0) html += buildShelf('New Anime',        anime,   'anime');
        if (mvs.length     > 0) html += buildShelf('New Music Videos', mvs,     'mv');
      }

      html += '</div>';
      el.innerHTML = html;
      if (heroItem) setupHeroCycle(heroPool);
      bindHoverPopup();
      setupArrows();
    } catch (err) {
      console.error('[Netflux] new-popular error:', err);
      el.innerHTML = '<div class="nf-empty">Could not load new content.</div>';
    }
  }

  /* Banner shown when there is no hero-eligible item */
  function buildNpBanner(count) {
    var subtitle = count > 0
      ? count + ' new title' + (count !== 1 ? 's' : '') + ' added to your library in the last 30 days'
      : 'Keep adding movies, shows and music to your library';
    return (
      '<div class="nf-np-banner">' +
        '<div class="nf-np-banner-inner">' +
          '<div class="nf-np-banner-label">Now streaming</div>' +
          '<h1 class="nf-np-banner-title">New &amp; Popular</h1>' +
          '<p class="nf-np-banner-sub">' + esc(subtitle) + '</p>' +
        '</div>' +
      '</div>'
    );
  }

  /* ════════════════════════════════════════════════
     5b. HERO VIDEO PREVIEW
  ════════════════════════════════════════════════ */

  function _previewsEnabled() {
    var cfg = typeof App !== 'undefined' ? App._initCfg : null;
    return cfg && cfg.videoPreviewsEnabled;
  }

  function _muteIcon(muted) {
    return muted
      ? '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
          '<polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"/>' +
          '<line x1="23" y1="9" x2="17" y2="15"/><line x1="17" y1="9" x2="23" y2="15"/>' +
        '</svg>'
      : '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
          '<polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"/>' +
          '<path d="M19.07 4.93a10 10 0 0 1 0 14.14"/>' +
          '<path d="M15.54 8.46a5 5 0 0 1 0 7.07"/>' +
        '</svg>';
  }

  function _heroVidFallback() {
    /* Revert to backdrop — called on error or explicit stop */
    var vid = document.getElementById('nf-hero-vid');
    if (vid) {
      vid.style.opacity = '0';
      vid.removeAttribute('src');
      vid.load();
    }
    var muteBtn = document.getElementById('nf-hero-mute');
    if (muteBtn) muteBtn.style.display = 'none';
  }

  function _stopHeroVideo() {
    clearTimeout(_heroVidTimer);
    _heroVidTimer = null;
    _heroVidFallback();
  }

  function _startHeroVideoDelayed(v) {
    if (!_previewsEnabled()) return;
    clearTimeout(_heroVidTimer);
    _heroVidTimer = setTimeout(function () { _playHeroVideo(v); }, 100);
  }

  function _playHeroVideo(v) {
    var vid = document.getElementById('nf-hero-vid');
    if (!vid) return;

    var type = v._nfMediaType === 'mv' ? 'musicvideo' : 'video';
    var id   = v.id;

    /* Attach error handler before setting src — covers 404 and mid-play failures */
    vid.onerror = _heroVidFallback;

    vid.loop  = true;
    vid.muted = _heroMuted;
    vid.src   = '/api/vpreviews/' + type + '/' + id;
    vid.load();

    var p = vid.play();
    if (p && p.then) {
      p.then(function () {
        /* Confirm the element still belongs to the current slide */
        if (vid !== document.getElementById('nf-hero-vid')) return;
        vid.style.opacity = '1';
        var muteBtn = document.getElementById('nf-hero-mute');
        if (muteBtn) {
          muteBtn.innerHTML = _muteIcon(_heroMuted);
          muteBtn.style.display = '';
        }
      }).catch(function () {
        /* play() rejected: autoplay policy, 404, or cancelled — show backdrop */
        _heroVidFallback();
      });
    }
  }

  window._nfToggleMute = function () {
    _heroMuted = !_heroMuted;
    var vid     = document.getElementById('nf-hero-vid');
    var muteBtn = document.getElementById('nf-hero-mute');
    if (vid) vid.muted = _heroMuted;
    if (muteBtn) muteBtn.innerHTML = _muteIcon(_heroMuted);
  };

  /* ════════════════════════════════════════════════
     6. HTML BUILDERS
  ════════════════════════════════════════════════ */
  function buildHero(v) {
    var bg  = v.backdropPath  ? '/videometa/' + v.backdropPath
            : v.thumbnailPath ? '/videothumb/' + v.thumbnailPath : '';
    var t   = esc(v.seriesName || v.title || '');
    var ov  = v.overview ? esc(v.overview.substring(0, 260) + (v.overview.length > 260 ? '…' : '')) : '';
    var yr  = v.year  ? '<span class="nf-hero-year">'  + v.year                           + '</span>' : '';
    var ge  = v.genre ? '<span class="nf-hero-genre">' + esc(v.genre.split(',')[0].trim()) + '</span>' : '';
    var pFn = buildPlayFn(v);
    var dFn = buildDetailFn(v);
    var age = v.contentRating ? '<div class="nf-hero-age" id="nf-hero-age">' + esc(v.contentRating) + '</div>' : '<div class="nf-hero-age" id="nf-hero-age" style="display:none"></div>';

    return (
      '<div class="nf-hero" id="nf-hero"' + (bg ? ' style="background-image:url(' + bg + ')"' : '') + '>' +
        /* Video preview overlay — opacity:0 until a preview plays; pointer-events:none
           ensures the transparent element never blocks clicks on the backdrop/content */
        '<video id="nf-hero-vid" muted playsinline preload="none" ' +
          'style="position:absolute;inset:0;width:100%;height:100%;object-fit:cover;' +
                 'z-index:0;opacity:0;transition:opacity .7s ease;pointer-events:none">' +
        '</video>' +
        '<div class="nf-hero-controls">' +
          '<button class="nf-hero-mute-btn" id="nf-hero-mute" style="display:none" onclick="window._nfToggleMute()" aria-label="Toggle mute">' +
            _muteIcon(true) +
          '</button>' +
          age +
        '</div>' +
        '<button class="nf-hero-nav nf-hero-nav-left"  id="nf-hero-prev" style="display:none" onclick="window._nfHeroNav(-1)">&#10094;</button>' +
        '<button class="nf-hero-nav nf-hero-nav-right" id="nf-hero-next" style="display:none" onclick="window._nfHeroNav(1)">&#10095;</button>' +
        '<div class="nf-hero-content">' +
          '<h1 class="nf-hero-title" id="nf-hero-title">' + t + '</h1>' +
          '<div class="nf-hero-meta" id="nf-hero-meta">' + ge + yr + '</div>' +
          (ov ? '<p class="nf-hero-overview" id="nf-hero-overview">' + ov + '</p>'
              : '<p class="nf-hero-overview" id="nf-hero-overview" style="display:none"></p>') +
          '<div class="nf-hero-btns">' +
            '<button class="nf-hero-play" id="nf-hero-play" onclick="' + pFn + '">' +
              '<svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" style="display:block"><polygon points="5,3 19,12 5,21"/></svg>' +
              'Play' +
            '</button>' +
            '<button class="nf-hero-info" id="nf-hero-info" onclick="' + dFn + '">' +
              '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" style="display:block">' +
                '<circle cx="12" cy="12" r="10"/>' +
                '<line x1="12" y1="11" x2="12" y2="17"/>' +
                '<circle cx="12" cy="7.5" r="1.3" fill="currentColor" stroke="none"/>' +
              '</svg>' +
              'More Info' +
            '</button>' +
          '</div>' +
        '</div>' +
        '<div class="nf-hero-dots" id="nf-hero-dots"></div>' +
      '</div>'
    );
  }

  function buildShelf(title, videos, mediaType) {
    if (!videos || videos.length === 0) return '';
    var cards = '';
    for (var i = 0; i < videos.length; i++) { cards += buildCard(videos[i], mediaType); }
    var navPage = mediaType === 'movie' ? 'movies'
                : mediaType === 'tv'    ? 'tvshows'
                : mediaType === 'anime' ? 'anime'
                : '';
    var moreHtml = navPage
      ? '<a href="#" class="nf-shelf-more" onclick="event.preventDefault();App.navigate(\'' + navPage + '\')">See all</a>'
      : '';
    var svgPrev = '<svg width="11" height="20" viewBox="0 0 11 20" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9,1 2,10 9,19"/></svg>';
    var svgNext = '<svg width="11" height="20" viewBox="0 0 11 20" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="2,1 9,10 2,19"/></svg>';
    return (
      '<div class="nf-shelf">' +
        '<div class="nf-shelf-header"><span class="nf-shelf-title">' + esc(title) + '</span>' + moreHtml + '</div>' +
        '<div class="nf-shelf-track">' +
          '<button class="nf-arrow nf-arrow-prev" style="display:none" onclick="window._nfScrollShelf(this,-1)" aria-label="Previous">' + svgPrev + '</button>' +
          '<div class="nf-shelf-scroll" onscroll="window._nfUpdateArrows(this)">' + cards + '</div>' +
          '<button class="nf-arrow nf-arrow-next" onclick="window._nfScrollShelf(this,1)" aria-label="Next">' + svgNext + '</button>' +
        '</div>' +
      '</div>'
    );
  }

  window._nfScrollShelf = function (btn, dir) {
    var track = btn.parentElement;
    var scr   = track && track.querySelector('.nf-shelf-scroll');
    if (!scr) return;
    scr.scrollBy({ left: dir * scr.clientWidth * 0.85, behavior: 'smooth' });
    setTimeout(function () { window._nfUpdateArrows(scr); }, 350);
  };

  window._nfUpdateArrows = function (scr) {
    var track = scr.parentElement;
    if (!track) return;
    var prev = track.querySelector('.nf-arrow-prev');
    var next = track.querySelector('.nf-arrow-next');
    var atStart = scr.scrollLeft < 10;
    var atEnd   = scr.scrollLeft >= scr.scrollWidth - scr.clientWidth - 10;
    if (prev) prev.style.display = atStart ? 'none' : '';
    if (next) next.style.display = atEnd   ? 'none' : '';
  };

  function setupArrows() {
    requestAnimationFrame(function () {
      document.querySelectorAll('.nf-shelf-scroll').forEach(function (scr) {
        window._nfUpdateArrows(scr);
      });
    });
  }

  var _nowMs = Date.now();
  var _45dMs = 30 * 24 * 60 * 60 * 1000;

  function isRecentlyAdded(v) {
    if (!v.dateAdded) return false;
    var added = new Date(v.dateAdded).getTime();
    return !isNaN(added) && (_nowMs - added) <= _45dMs;
  }

  function buildCard(v, mediaType) {
    var id  = v.id || 0;
    var img = '';
    if (mediaType === 'mv') {
      img = v.thumbnailPath ? '/mvthumb/' + v.thumbnailPath : '';
    } else {
      img = v.backdropPath  ? '/videometa/'  + v.backdropPath
          : v.posterPath    ? '/videometa/'  + v.posterPath
          : v.thumbnailPath ? '/videothumb/' + v.thumbnailPath
          : '';
    }
    v._nfMediaType = mediaType;
    _cardData[id]  = v;

    var badge = isRecentlyAdded(v) ? '<div class="nf-badge-new">Recently Added</div>' : '';

    return (
      '<div class="nf-card" data-nf-id="' + id + '">' +
        '<div class="nf-card-img">' +
          (img
            ? '<img src="' + esc(img) + '" loading="lazy" alt="" onerror="this.style.display=\'none\'">'
            : '<div class="nf-card-noimg">&#127909;</div>') +
          badge +
        '</div>' +
        '<div class="nf-card-title">' + esc(v.seriesName || v.title || '') + '</div>' +
      '</div>'
    );
  }

  /* Build onclick string safe for inline attributes */
  function buildPlayFn(v) {
    if (v._nfMediaType === 'mv') return 'App.playMusicVideo(' + v.id + ')';
    if (v.seriesName) {
      var sn = v.seriesName.replace(/\\/g, '\\\\').replace(/'/g, "\\'");
      return 'App.openSeriesDetail(\'' + sn + '\'' + (v.mediaType === 'anime' ? ",'anime'" : '') + ')';
    }
    return 'App.openVideoDetail(' + v.id + ')';
  }

  function buildDetailFn(v) {
    if (v.seriesName) {
      var sn = v.seriesName.replace(/\\/g, '\\\\').replace(/'/g, "\\'");
      return 'App.openSeriesDetail(\'' + sn + '\'' + (v.mediaType === 'anime' ? ",'anime'" : '') + ')';
    }
    if (v._nfMediaType === 'mv') return 'App.openMvDetail(' + v.id + ')';
    return 'App.openVideoDetail(' + v.id + ')';
  }

  /* ════════════════════════════════════════════════
     7. HERO CYCLE
  ════════════════════════════════════════════════ */
  function updateHeroSlide(v) {
    _stopHeroVideo();
    var hero = document.getElementById('nf-hero');
    if (!hero) return;
    hero.style.opacity = '0.5';
    setTimeout(function () {
      if (!document.getElementById('nf-hero')) return;
      hero.style.opacity = '1';
      _startHeroVideoDelayed(v);
    }, 200);

    var bg = v.backdropPath  ? '/videometa/'  + v.backdropPath
           : v.thumbnailPath ? '/videothumb/' + v.thumbnailPath : '';
    if (bg) hero.style.backgroundImage = 'url(' + bg + ')';

    var tEl = document.getElementById('nf-hero-title');
    if (tEl) tEl.textContent = v.seriesName || v.title || '';

    var ovEl = document.getElementById('nf-hero-overview');
    if (ovEl) {
      ovEl.textContent   = v.overview ? v.overview.substring(0, 260) : '';
      ovEl.style.display = ovEl.textContent ? '' : 'none';
    }

    var metaEl = document.getElementById('nf-hero-meta');
    if (metaEl) {
      metaEl.innerHTML =
        (v.genre ? '<span class="nf-hero-genre">' + esc(v.genre.split(',')[0].trim()) + '</span>' : '') +
        (v.year  ? '<span class="nf-hero-year">'  + v.year + '</span>' : '');
    }

    var pBtn  = document.getElementById('nf-hero-play');
    var iBtn  = document.getElementById('nf-hero-info');
    var ageEl = document.getElementById('nf-hero-age');
    if (pBtn)  pBtn.setAttribute('onclick', buildPlayFn(v));
    if (iBtn)  iBtn.setAttribute('onclick', buildDetailFn(v));
    if (ageEl) {
      if (v.contentRating) {
        ageEl.textContent    = v.contentRating;
        ageEl.style.display  = '';
      } else {
        ageEl.style.display  = 'none';
      }
    }

    document.querySelectorAll('.nf-hero-dot').forEach(function (d, i) {
      d.classList.toggle('nf-hero-dot-active', i === _heroCycleIdx);
    });
  }

  function _startCycleTimer() {
    _heroCycleTimer = setInterval(function () {
      if (!document.getElementById('nf-hero')) { clearHeroCycle(); return; }
      _heroCycleIdx = (_heroCycleIdx + 1) % _heroCycleItems.length;
      updateHeroSlide(_heroCycleItems[_heroCycleIdx]);
    }, 20000);
  }

  function setupHeroCycle(items) {
    clearHeroCycle();
    _heroCycleItems = items || [];
    _heroCycleIdx   = 0;
    var multi = _heroCycleItems.length > 1;

    var prev = document.getElementById('nf-hero-prev');
    var next = document.getElementById('nf-hero-next');
    if (prev) prev.style.display = multi ? '' : 'none';
    if (next) next.style.display = multi ? '' : 'none';

    var dots = document.getElementById('nf-hero-dots');
    if (dots) {
      if (multi) {
        var dh = '', dc = Math.min(_heroCycleItems.length, 8);
        for (var i = 0; i < dc; i++) {
          dh += '<button class="nf-hero-dot' + (i === 0 ? ' nf-hero-dot-active' : '') +
                '" onclick="window._nfHeroNavTo(' + i + ')"></button>';
        }
        dots.innerHTML = dh;
      } else {
        dots.innerHTML = '';
      }
    }
    if (multi) _startCycleTimer();
    if (_heroCycleItems.length > 0) _startHeroVideoDelayed(_heroCycleItems[0]);
  }

  function clearHeroCycle() {
    _stopHeroVideo();
    clearInterval(_heroCycleTimer);
    _heroCycleTimer = null;
  }

  window._nfHeroNav = function (dir) {
    if (!_heroCycleItems.length) return;
    clearInterval(_heroCycleTimer);
    _heroCycleIdx = ((_heroCycleIdx + dir) + _heroCycleItems.length) % _heroCycleItems.length;
    updateHeroSlide(_heroCycleItems[_heroCycleIdx]);
    _startCycleTimer();
  };

  window._nfHeroNavTo = function (idx) {
    if (!_heroCycleItems.length) return;
    clearInterval(_heroCycleTimer);
    _heroCycleIdx = idx % _heroCycleItems.length;
    updateHeroSlide(_heroCycleItems[_heroCycleIdx]);
    _startCycleTimer();
  };

  /* ════════════════════════════════════════════════
     8. HOVER POPUP
  ════════════════════════════════════════════════ */
  function ensurePopup() {
    if (document.getElementById('nf-popup')) return;
    var p = document.createElement('div');
    p.id = 'nf-popup';
    p.style.display = 'none';
    document.body.appendChild(p);
    p.addEventListener('mouseenter', function () { clearTimeout(_popupTimer); });
    p.addEventListener('mouseleave', function () { hidePopup(); });
  }

  function bindHoverPopup() {
    var mc = document.getElementById('main-content');
    if (!mc || mc._nfHoverBound) return;
    mc._nfHoverBound = true;

    mc.addEventListener('mouseover', function (e) {
      var card = e.target.closest && e.target.closest('.nf-card');
      if (!card || card === _popupActiveCard) return;
      _popupActiveCard = card;
      clearTimeout(_popupTimer);
      _popupTimer = setTimeout(function () { showPopup(card); }, 180);
    });

    mc.addEventListener('mouseout', function (e) {
      var card = e.target.closest && e.target.closest('.nf-card');
      if (!card) return;
      var rel = e.relatedTarget;
      var popup = document.getElementById('nf-popup');
      if (popup && (popup === rel || popup.contains(rel))) return;
      if (card.contains(rel)) return;
      clearTimeout(_popupTimer);
      _popupActiveCard = null;
    });
  }

  function showPopup(card) {
    var popup = document.getElementById('nf-popup');
    if (!popup) return;
    var id = parseInt(card.getAttribute('data-nf-id'), 10);
    var v  = _cardData[id];
    if (!v) return;

    fillPopup(popup, v);
    popup.style.display = 'block';

    /* Position after paint — Netflix style: popup image centred on the card */
    requestAnimationFrame(function () {
      var rect = card.getBoundingClientRect();
      var pw   = 400;
      var ph   = popup.offsetHeight;
      var gap  = 6;

      /* Horizontal: centre over card, clamped to viewport */
      var left = rect.left + rect.width / 2 - pw / 2;
      left = Math.max(gap, Math.min(left, window.innerWidth - pw - gap));
      popup.style.left = left + 'px';

      /* Vertical: align popup image centre with card centre.
         Popup image is 16:9 at pw wide, so imgH ≈ pw×9/16 = 180px.
         Place so that midpoint of image sits at midpoint of card. */
      var imgH = Math.round(pw * 9 / 16);
      var top  = rect.top + rect.height / 2 - imgH / 2;
      /* Clamp so popup stays fully within viewport */
      top = Math.max(gap, Math.min(top, window.innerHeight - ph - gap));
      popup.style.top = top + 'px';
    });
  }

  function fillPopup(popup, v) {
    var mt  = v._nfMediaType || v.mediaType || '';
    var img = '';
    if (mt === 'mv') {
      img = v.thumbnailPath ? '/mvthumb/' + v.thumbnailPath : '';
    } else {
      img = v.backdropPath  ? '/videometa/'  + v.backdropPath
          : v.posterPath    ? '/videometa/'  + v.posterPath
          : v.thumbnailPath ? '/videothumb/' + v.thumbnailPath
          : '';
    }

    var title   = esc(v.seriesName || v.title || '');
    var genres  = v.genre ? v.genre.split(',').map(function (g) { return g.trim(); }).slice(0, 3) : [];
    var year    = v.year || '';
    var rating  = v.rating > 0 ? Math.round(v.rating * 10) + '% Match' : '';
    var seasons = (v.seasonCount > 0)
      ? v.seasonCount + ' Season' + (v.seasonCount !== 1 ? 's' : '')
      : '';

    var pFn = buildPlayFn(v);
    var dFn = buildDetailFn(v);

    var inWl    = App._watchlistIds && App._watchlistIds.has(v.id);
    var wlIcon  = inWl
      ? '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>'
      : '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>';
    var wlTitle = inWl ? 'In Watchlist' : 'Add to Watchlist';

    var genreHtml = '';
    genres.forEach(function (g, i) {
      if (i > 0) genreHtml += '<span class="nf-popup-dot"></span>';
      genreHtml += esc(g);
    });

    var imgHtml = img
      ? '<img class="nf-popup-img" src="' + esc(img) + '" alt="" onerror="this.style.display=\'none\'">'
      : '<div class="nf-popup-img-ph">&#127909;</div>';

    popup.innerHTML =
      imgHtml +
      '<div class="nf-popup-body">' +
        '<div class="nf-popup-title">' + title + '</div>' +
        '<div class="nf-popup-btns">' +
          /* Play */
          '<button class="nf-popup-btn nf-popup-btn-play" onclick="' + pFn + '" title="Play">' +
            '<svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" style="display:block"><polygon points="5,3 19,12 5,21"/></svg>' +
          '</button>' +
          /* Watchlist toggle */
          '<button class="nf-popup-btn" id="nf-pop-wl" onclick="window._nfToggleWl(this,' + v.id + ')" title="' + wlTitle + '">' +
            wlIcon +
          '</button>' +
          /* Rate (opens detail) */
          '<button class="nf-popup-btn" onclick="' + dFn + '" title="Rate">' +
            '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="display:block"><path d="M14 9V5a3 3 0 0 0-3-3l-4 9v11h11.28a2 2 0 0 0 2-1.7l1.38-9a2 2 0 0 0-2-2.3H14z"/><path d="M7 22H4a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2h3"/></svg>' +
          '</button>' +
          /* Episodes & info — clean bold chevron, no inner circle */
          '<button class="nf-popup-btn nf-popup-btn-detail" onclick="' + dFn + '" title="Episodes &amp; info">' +
            '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" style="display:block"><polyline points="6 9 12 15 18 9"/></svg>' +
          '</button>' +
        '</div>' +
        /* Meta row */
        '<div class="nf-popup-meta">' +
          (rating  ? '<span class="nf-popup-match">' + rating + '</span>' : '') +
          (year    ? '<span class="nf-popup-year">'  + year   + '</span>' : '') +
          (seasons ? '<span class="nf-popup-seasons">' + esc(seasons) + '</span>' : '') +
          '<span class="nf-popup-hd">HD</span>' +
        '</div>' +
        /* Genre tags */
        (genreHtml ? '<div class="nf-popup-genres">' + genreHtml + '</div>' : '') +
      '</div>';
  }

  function hidePopup() {
    var p = document.getElementById('nf-popup');
    if (p) p.style.display = 'none';
    _popupActiveCard = null;
    clearTimeout(_popupTimer);
  }

  /* Watchlist toggle — called from popup button inline onclick */
  window._nfToggleWl = function (btn, videoId) {
    if (!App) return;
    btn.disabled = true;
    fetch('/api/videos/' + videoId + '/watchlist', { method: 'POST' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        if (!data) { btn.disabled = false; return; }
        if (data.inWatchlist) {
          if (App._watchlistIds) App._watchlistIds.add(videoId);
          btn.innerHTML =
            '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>';
          btn.title = 'In Watchlist';
        } else {
          if (App._watchlistIds) App._watchlistIds.delete(videoId);
          btn.innerHTML =
            '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>';
          btn.title = 'Add to Watchlist';
        }
        btn.disabled = false;
      })
      .catch(function () { btn.disabled = false; });
  };

  /* ════════════════════════════════════════════════
     9. DATA HELPERS
  ════════════════════════════════════════════════ */
  function extractVids(data) {
    if (!data) return [];
    if (data.items)  return data.items;
    if (data.videos) return data.videos;
    if (Array.isArray(data)) return data;
    return [];
  }

  function extractGenres(data) {
    if (!data) return [];
    var arr = data.genres || (Array.isArray(data) ? data : []);
    return arr.map(function (g) { return typeof g === 'string' ? g : (g.name || ''); })
              .filter(function (g) { return !!g; });
  }

  function groupByGenre(videos) {
    var result = {};
    videos.forEach(function (v) {
      if (!v.genre) return;
      var g = v.genre.split(',')[0].trim();
      if (!g) return;
      if (!result[g]) result[g] = [];
      result[g].push(v);
    });
    return result;
  }

  /* ════════════════════════════════════════════════
     10. PATCH App METHODS
  ════════════════════════════════════════════════ */
  function guardMainContent() {
    var mc = document.getElementById('main-content');
    if (!mc || mc._nfGuarded) return;
    mc._nfGuarded = true;

    /* Walk prototype chain to find native innerHTML descriptor */
    var proto = mc, desc = null;
    while (proto) {
      desc = Object.getOwnPropertyDescriptor(proto, 'innerHTML');
      if (desc && desc.set) break;
      proto = Object.getPrototypeOf(proto);
    }
    if (!desc || !desc.set) return;

    var nGet = desc.get, nSet = desc.set;

    Object.defineProperty(mc, 'innerHTML', {
      configurable: true,
      get: function () { return nGet.call(mc); },
      set: function (val) {
        var cp = typeof App !== 'undefined' ? App.currentPage : null;
        var weOwn = (cp === 'home' || cp === 'movies' || cp === 'tvshows' || cp === 'anime');
        if (weOwn) {
          /* Reject the original grid renders; let our renders through */
          if (val.indexOf('class="home-section"') !== -1 ||
              val.indexOf('class="filter-bar"')    !== -1) return;
        }
        var isNf = val === '' ||
                   val.indexOf('nf-loading') !== -1 ||
                   val.indexOf('nf-hero')    !== -1 ||
                   val.indexOf('nf-shelves') !== -1;
        mc.classList.toggle('nf-detail-view', !isNf && val.length > 0);
        if (!isNf) hidePopup();
        nSet.call(mc, val);
      }
    });
  }

  function phase1Patch() {
    if (_p1Done) return;
    if (typeof App === 'undefined' || typeof App.renderHome !== 'function') return;
    _p1Done = true;

    guardMainContent();

    App.renderHome = async function (el) {
      syncActive('home');
      await renderHome(el || document.getElementById('main-content'));
    };
    App.renderMovies = async function (el) {
      syncActive('movies');
      await renderMedia('movie', el || document.getElementById('main-content'));
    };
    App.renderTvShows = async function (el) {
      syncActive('tvshows');
      await renderMedia('tv', el || document.getElementById('main-content'));
    };
    App.renderAnime = async function (el) {
      syncActive('anime');
      await renderMedia('anime', el || document.getElementById('main-content'));
    };

    var _origRenderPage = App.renderPage.bind(App);
    App.renderPage = async function (page) {
      var content = document.getElementById('main-content');
      if (!content) return _origRenderPage(page);
      content.innerHTML = '';
      if (page === 'home')         { await App.renderHome(content);                      return; }
      if (page === 'movies')       { await App.renderMovies(content);                    return; }
      if (page === 'tvshows')      { await App.renderTvShows(content);                   return; }
      if (page === 'anime')        { await App.renderAnime(content);                     return; }
      if (page === 'documentary')  { syncActive('documentary'); await renderMedia('documentary', content); return; }
      if (page === 'nfnew')        { await renderNewPopular(content);                    return; }
      if (page === 'nfgenre')      { await renderGenreView(_nfCurrentGenre, content);    return; }
      hidePopup();
      return _origRenderPage(page);
    };
  }

  function phase2Patch() {
    if (_p2Done) return;
    if (typeof App === 'undefined' || App._initCfg === undefined) return;
    _p2Done = true;

    /* Patch navigate for active-tab sync + hero/popup cleanup */
    var _origNav = App.navigate.bind(App);
    App.navigate = function (page) {
      clearHeroCycle();
      hidePopup();
      syncActive(page);
      return _origNav(page);
    };

    buildTabs();
    buildAccount();

    if (App.currentPage) syncActive(App.currentPage);

    /* Re-render current page in Netflux layout */
    var cp = App.currentPage;
    if (cp === 'home' || cp === 'movies' || cp === 'tvshows' || cp === 'anime' || cp === 'documentary') {
      App.renderPage(cp);
    }
  }

  /* ════════════════════════════════════════════════
     11. BOOT — polling until App is ready
  ════════════════════════════════════════════════ */
  buildNav();
  ensurePopup();

  var _ticks = 0;
  var _iv = setInterval(function () {
    if (!_p1Done && typeof App !== 'undefined' && typeof App.renderHome === 'function') {
      phase1Patch();
    }
    if (!_p2Done && typeof App !== 'undefined' && App._initCfg !== undefined) {
      phase2Patch();
      clearInterval(_iv);
      return;
    }
    if (++_ticks > 200) clearInterval(_iv);
  }, 50);

})();
