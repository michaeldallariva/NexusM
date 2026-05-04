/* ------------------------------------------------------------------
   NexusM Custom Template 2 - "Amazing!"
   Amazon Prime Video-style layout: hero + genre rows.
   Covers Movies / TV Shows / Documentaries / Anime only.
   ------------------------------------------------------------------ */
(function () {
  'use strict';

  var _heroCycleTimer = null;
  var _heroCycleItems = [];
  var _heroCycleIdx   = 0;

  /* Render-generation counter — incremented each time a new amazingRender* call
     starts.  After the async fetch, each render checks if its generation is still
     current; if not (a newer render superseded it) it aborts without touching the
     DOM or calling setupHeroCycle.  This prevents the double-render that happens
     on first load (patchApp + navigate both fire a render concurrently). */
  var _amRenderGen = 0;

  /* ── SVG icon helper ───────────────────────────────────────────── */
  function icon(name, size) {
    size = size || 16;
    return '<svg width="' + size + '" height="' + size + '" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="display:block;flex-shrink:0"><use href="#icon-' + name + '"/></svg>';
  }

  /* ════════════════════════════════════════════════
     1. NAV BAR
  ════════════════════════════════════════════════ */
  function buildNav() {
    var topbar = document.querySelector('header.topbar');
    if (!topbar || document.getElementById('am-bar')) return;

    var bar = document.createElement('div');
    bar.id = 'am-bar';

    /* Logo */
    var logo = document.createElement('a');
    logo.id = 'am-logo';
    logo.href = '#';
    logo.innerHTML = '<span class="am-logo-n">Nexus</span><span class="am-logo-m">M</span>';
    logo.onclick = function (e) { e.preventDefault(); amNav('home'); };
    bar.appendChild(logo);

    /* Tab strip (populated once App is ready) */
    var tabs = document.createElement('nav');
    tabs.id = 'am-tabs';
    bar.appendChild(tabs);

    /* Right section
       SAFE: move existing children into a hidden holder so #sidebar-expand-btn
       and other elements app.js needs (bindSidebar, bindSearch, etc.) stay in
       the DOM. Never use innerHTML='' — that destroys them before app.js binds. */
    var right = document.createElement('div');
    right.id = 'am-right';

    /* 9-dot categories button + panel */
    var catsBtn = document.createElement('button');
    catsBtn.id = 'am-cats-btn';
    catsBtn.className = 'am-cats-btn topbar-btn';
    catsBtn.title = 'Browse categories';
    catsBtn.innerHTML =
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

    /* Panel appended to body with fixed positioning — avoids stacking/overflow clipping */
    var catsPanel = document.createElement('div');
    catsPanel.id = 'am-cats-panel';
    catsPanel.style.display = 'none';
    document.body.appendChild(catsPanel);

    right.appendChild(catsBtn);

    catsBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      var open = catsPanel.style.display !== 'none';
      if (open) {
        catsPanel.style.display = 'none';
      } else {
        var r = catsBtn.getBoundingClientRect();
        catsPanel.style.top   = (r.bottom + 8) + 'px';
        catsPanel.style.right = (window.innerWidth - r.right) + 'px';
        catsPanel.style.left  = 'auto';
        catsPanel.style.display = 'block';
        buildCatsPanel();
      }
    });
    document.addEventListener('click', function () {
      if (catsPanel) catsPanel.style.display = 'none';
    });
    catsPanel.addEventListener('click', function (e) { e.stopPropagation(); });

    var holder = document.createElement('div');
    holder.id = 'am-orig-holder';
    holder.style.cssText = 'display:none!important;position:absolute;pointer-events:none';

    while (topbar.firstChild) { holder.appendChild(topbar.firstChild); }

    var sb = holder.querySelector('.search-box');
    var ta = holder.querySelector('.topbar-actions');
    if (sb) right.appendChild(sb);
    if (ta) right.appendChild(ta);
    bar.appendChild(right);

    /* Amazon Prime-style search: icon toggles the input field */
    if (sb) {
      var searchIcon = sb.querySelector('.search-icon');
      var searchInp  = sb.querySelector('input');
      if (searchIcon) {
        searchIcon.addEventListener('click', function (e) {
          e.stopPropagation();
          var isOpen = sb.classList.toggle('am-search-open');
          if (isOpen && searchInp) searchInp.focus();
        });
      }
      if (searchInp) {
        searchInp.addEventListener('blur', function () {
          if (!searchInp.value) sb.classList.remove('am-search-open');
        });
      }
      sb.addEventListener('click', function (e) { e.stopPropagation(); });
      /* Close search when clicking anywhere outside */
      document.addEventListener('click', function () {
        if (sb && !sb.querySelector('input').value) sb.classList.remove('am-search-open');
      });
    }

    topbar.appendChild(bar);
    topbar.appendChild(holder);
  }

  function buildTabs() {
    var tabsEl = document.getElementById('am-tabs');
    if (!tabsEl || tabsEl._amBuilt) return;
    tabsEl._amBuilt = true;

    var cfg       = (typeof App !== 'undefined') ? App._initCfg : null;
    var showAnime = cfg && cfg.showAnime;
    var showMV    = cfg && cfg.showMusicVideos;

    var pages = [
      { page: 'home',    label: 'Home' },
      { page: 'movies',  label: 'Movies' },
      { page: 'tvshows', label: 'TV Shows' },
      { page: 'amdocs',  label: 'Documentaries' },
    ];
    if (showMV)    pages.push({ page: 'musicvideos', label: 'Music Videos' });
    if (showAnime) pages.push({ page: 'anime',       label: 'Animes' });

    pages.forEach(function (item) {
      var a = document.createElement('a');
      a.href = '#';
      a.className = 'am-tab';
      a.setAttribute('data-am-page', item.page);
      a.textContent = item.label;
      a.onclick = function (e) { e.preventDefault(); amNav(item.page); };
      tabsEl.appendChild(a);
    });
  }

  /* ════════════════════════════════════════════════
     2. CATEGORIES PANEL — Amazon Prime style
        Left:  GENRES (top 10, 2-col, no Documentaries)
        Right: NAVIGATE (Settings + pages)
  ════════════════════════════════════════════════ */
  var _cachedGenres = null; /* cache after first fetch */

  function buildCatsPanel() {
    var panel = document.getElementById('am-cats-panel');
    if (!panel) return;

    var cfg       = typeof App !== 'undefined' ? App._initCfg : null;
    var showAnime = cfg && cfg.showAnime;
    var showMV    = cfg && cfg.showMusicVideos;

    /* Build right-column navigate links */
    var navLinks = [
      { label: 'Movies',        onclick: function () { amNav('movies');      } },
      { label: 'TV Shows',      onclick: function () { amNav('tvshows');     } },
      { label: 'Documentaries', onclick: function () { amNav('amdocs');      } },
    ];
    if (showMV)    navLinks.push({ label: 'Music Videos', onclick: function () { amNav('musicvideos'); } });
    if (showAnime) navLinks.push({ label: 'Animes',       onclick: function () { amNav('anime');       } });
    navLinks.push({ label: 'Settings', onclick: function () { amNav('settings'); }, isSettings: true });

    /* Shell — two-column layout */
    panel.innerHTML =
      '<div class="am-cats-columns">' +
        '<div class="am-cats-col" id="am-cats-col-genres">' +
          '<div class="am-cats-col-label">Genres</div>' +
          '<div class="am-cats-genre-list" id="am-cats-genre-list">' +
            '<span class="am-cats-loading-inline">Loading…</span>' +
          '</div>' +
        '</div>' +
        '<div class="am-cats-col am-cats-col-right" id="am-cats-col-nav">' +
          '<div class="am-cats-col-label">Navigate</div>' +
        '</div>' +
      '</div>';

    /* Populate navigate column */
    var navCol = document.getElementById('am-cats-col-nav');
    if (navCol) {
      navLinks.forEach(function (lnk) {
        var btn = document.createElement('button');
        btn.className = 'am-cats-nav-link' + (lnk.isSettings ? ' am-cats-nav-settings' : '');
        btn.textContent = lnk.label;
        btn.onclick = function () {
          panel.style.display = 'none';
          lnk.onclick();
        };
        navCol.appendChild(btn);
      });
    }

    /* Use cached genres if available */
    if (_cachedGenres) {
      renderGenreList(_cachedGenres);
      return;
    }

    /* Fetch movie + TV genres, merge + deduplicate, exclude Documentary */
    Promise.all([
      App.api('videos/genres?mediaType=movie').catch(function () { return []; }),
      App.api('videos/genres?mediaType=tv').catch(function () { return []; })
    ]).then(function (results) {
      var movieG = extractGenres(results[0]);
      var tvG    = extractGenres(results[1]);

      /* Merge, deduplicate, exclude documentary variants, take top 10 */
      var seen   = {};
      var merged = [];
      movieG.concat(tvG).forEach(function (g) {
        var key = g.toLowerCase();
        if (seen[key]) return;
        if (/documentar/i.test(g)) return;
        seen[key] = true;
        merged.push(g);
      });
      _cachedGenres = merged.slice(0, 10);
      renderGenreList(_cachedGenres);
    });
  }

  function renderGenreList(genres) {
    var listEl = document.getElementById('am-cats-genre-list');
    if (!listEl) return;

    if (!genres || genres.length === 0) {
      listEl.innerHTML = '<span class="am-cats-loading-inline" style="color:var(--text-muted)">No genres found.</span>';
      return;
    }

    /* Split into two sub-columns */
    var half  = Math.ceil(genres.length / 2);
    var col1  = genres.slice(0, half);
    var col2  = genres.slice(half);

    var html = '<div class="am-cats-genre-cols">';
    html += '<div class="am-cats-genre-col">';
    col1.forEach(function (g) {
      html += '<button class="am-cats-genre-link" data-genre="' + esc(g) + '">' + esc(g) + '</button>';
    });
    html += '</div><div class="am-cats-genre-col">';
    col2.forEach(function (g) {
      html += '<button class="am-cats-genre-link" data-genre="' + esc(g) + '">' + esc(g) + '</button>';
    });
    html += '</div></div>';

    listEl.innerHTML = html;

    /* Bind clicks — use 'movie' as primary mediaType for genre navigation */
    listEl.querySelectorAll('.am-cats-genre-link').forEach(function (btn) {
      btn.onclick = function () {
        var genre = btn.getAttribute('data-genre');
        document.getElementById('am-cats-panel').style.display = 'none';
        amazingRenderGenre('movie', genre);
      };
    });
  }

  /* ════════════════════════════════════════════════
     3. ACCOUNT DROPDOWN
  ════════════════════════════════════════════════ */
  function buildAccountDropdown() {
    var ta = document.getElementById('am-right');
    if (!ta || document.getElementById('am-account-dropdown')) return;

    var actions = ta.querySelector('.topbar-actions');
    if (!actions) return;
    var btns = actions.querySelectorAll('button');
    var userBtn = null;
    for (var i = btns.length - 1; i >= 0; i--) {
      var t = btns[i].title || '';
      if (/account|profile|user|sign|logout/i.test(t) || i === btns.length - 1) {
        userBtn = btns[i];
        break;
      }
    }
    if (!userBtn) return;

    var dropdown = document.createElement('div');
    dropdown.id = 'am-account-dropdown';
    dropdown.style.display = 'none';
    actions.style.position = 'relative';
    actions.appendChild(dropdown);

    userBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      var d = document.getElementById('am-account-dropdown');
      if (!d) return;
      var open = d.style.display !== 'none';
      d.style.display = open ? 'none' : 'block';
      if (!open) refreshDropdown(d);
    });
    document.addEventListener('click', function () {
      var d = document.getElementById('am-account-dropdown');
      if (d) d.style.display = 'none';
    });
  }

  function refreshDropdown(d) {
    var isAdmin  = typeof App !== 'undefined' && App.userRole === 'admin';
    var username = typeof App !== 'undefined' ? (App.userDisplayName || App.userName || 'Account') : 'Account';
    var settingsIcon = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>';
    var signoutIcon = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>';
    d.innerHTML =
      '<div class="am-dropdown-user">' + esc(username) + '</div>' +
      '<div class="am-dropdown-divider"></div>' +
      (isAdmin
        ? '<div class="am-dropdown-item" id="am-dd-settings">' + settingsIcon + 'Settings</div>' +
          '<div class="am-dropdown-divider"></div>'
        : '') +
      '<div class="am-dropdown-item am-dd-danger" id="am-dd-signout">' + signoutIcon + 'Sign out</div>';

    var settingsBtn = document.getElementById('am-dd-settings');
    if (settingsBtn) {
      settingsBtn.onclick = function (e) {
        e.stopPropagation();
        d.style.display = 'none';
        amNav('settings');
      };
    }
    var signoutBtn = document.getElementById('am-dd-signout');
    if (signoutBtn) {
      signoutBtn.onclick = function (e) {
        e.stopPropagation();
        d.style.display = 'none';
        if (typeof App !== 'undefined' && App.logout) App.logout();
      };
    }
  }

  /* ════════════════════════════════════════════════
     4. NAVIGATION
  ════════════════════════════════════════════════ */
  function amNav(page) {
    syncActive(page);
    clearHeroCycle();
    if (page === 'amdocs') {
      if (typeof App !== 'undefined') {
        App._amVirtualPage = 'amdocs';
        App.currentPage = 'tvshows';
        var t = document.getElementById('page-title');
        if (t) t.innerHTML = '<span>Documentaries</span>';
        amazingRender('documentary', document.getElementById('main-content'));
      }
    } else {
      if (typeof App !== 'undefined') {
        App._amVirtualPage = null;
        App.navigate(page);
      }
    }
  }

  function syncActive(page) {
    document.querySelectorAll('.am-tab').forEach(function (a) {
      var p = a.getAttribute('data-am-page');
      a.classList.toggle('am-tab-active', p === page);
    });
  }

  /* ════════════════════════════════════════════════
     5. AMAZING RENDER ENGINE
  ════════════════════════════════════════════════ */
  async function amazingRender(mediaType, el, genreFilter) {
    if (!el) el = document.getElementById('main-content');
    if (!el) return;
    var gen = ++_amRenderGen;
    el.innerHTML = '<div class="am-loading">Loading…</div>';
    try {
      var [recentData, genreData] = await Promise.all([
        App.api('videos?mediaType=' + encodeURIComponent(mediaType) + '&sort=recent&limit=300&grouped=true'),
        App.api('videos/genres?mediaType=' + encodeURIComponent(mediaType))
      ]);
      if (_amRenderGen !== gen) return; /* superseded by a newer render — abort */

      var allVideos = extractVideos(recentData);
      var genres    = extractGenres(genreData);

      /* Hero — for docs/videos without TMDB metadata, fall back to FFmpeg thumbnails */
      var heroPool = allVideos.filter(function (v) { return v.backdropPath && v.posterPath; });
      if (!heroPool.length) heroPool = allVideos.filter(function (v) { return v.backdropPath; });
      if (!heroPool.length) heroPool = allVideos.filter(function (v) { return v.thumbnailPath; });
      var heroItem = heroPool[0] || allVideos[0] || null;

      var html = heroItem ? buildHero(heroItem, mediaType) : '';
      html += '<div class="am-genre-rows">';

      /* Genre filter mode: single genre header + back button */
      if (genreFilter) {
        var filtered = allVideos.filter(function (v) {
          return v.genre && v.genre.split(',').some(function (g) { return g.trim() === genreFilter; });
        });
        html += '<div class="am-row-back">' +
          '<button class="am-row-back-btn" onclick="App._amazingUnfilter(\'' + esc(mediaType) + '\')">' +
            icon('arrow-left', 16) + ' Back' +
          '</button>' +
          '<div class="am-row-back-label">' + esc(genreFilter) + '</div>' +
        '</div>';
        if (filtered.length > 0) {
          html += buildGenreRow(genreFilter, filtered, mediaType);
        } else {
          html += '<div class="am-empty">No titles found for "' + esc(genreFilter) + '".</div>';
        }
      } else {
        var renderedRows = 0;
        if (genres.length > 0) {
          var byGenre = groupByGenre(allVideos);
          genres.forEach(function (g) {
            var vids = byGenre[g] || [];
            if (vids.length < 1) return;
            html += buildGenreRow(g, vids.slice(0, 24), mediaType);
            renderedRows++;
          });
        }
        if (renderedRows === 0) {
          if (allVideos.length > 0) {
            html += buildGenreRow('All', allVideos.slice(0, 48), mediaType);
          } else {
            html += '<div class="am-empty">No content found in your library.</div>';
          }
        }
      }

      html += '</div>';
      el.innerHTML = html;
      setupHeroCycle(heroPool);
    } catch (err) {
      console.error('[Amazing] render error:', err);
      el.innerHTML = '<div class="am-empty">Could not load content.</div>';
    }
  }

  function amazingRenderGenre(mediaType, genre) {
    var page = mediaType === 'movie' ? 'movies' : 'tvshows';
    syncActive(page);
    clearHeroCycle();
    if (typeof App !== 'undefined') {
      App.currentPage = page;
      App._amVirtualPage = null;
    }
    amazingRender(mediaType, document.getElementById('main-content'), genre);
    /* Store unfilter callback */
    if (typeof App !== 'undefined') {
      App._amazingUnfilter = function (mt) {
        amazingRender(mt, document.getElementById('main-content'));
      };
    }
  }

  async function amazingRenderHome(el) {
    if (!el) el = document.getElementById('main-content');
    if (!el) return;
    var gen = ++_amRenderGen;
    el.innerHTML = '<div class="am-loading">Loading…</div>';
    try {
      var cfg       = typeof App !== 'undefined' ? App._initCfg : null;
      var showAnime = cfg && cfg.showAnime;

      var requests = [
        App.api('videos?mediaType=movie&sort=recent&limit=200&grouped=true'),
        App.api('videos?mediaType=tv&sort=recent&limit=150&grouped=true'),
        App.api('videos/continue-watching').catch(function () { return []; })
      ];
      if (showAnime) {
        requests.push(App.api('videos?mediaType=anime&sort=recent&limit=80&grouped=true'));
      }

      var results = await Promise.all(requests);
      if (_amRenderGen !== gen) return; /* superseded by a newer render — abort */
      var movies  = extractVideos(results[0]);
      var tvShows = extractVideos(results[1]);
      var cw      = Array.isArray(results[2]) ? results[2] : [];
      var anime   = showAnime ? extractVideos(results[3]) : [];

      /* Filter CW to only video types we show */
      cw = cw.filter(function (v) {
        var mt = v.mediaType || '';
        return mt === 'movie' || mt === 'tv' || mt === 'documentary' || mt === 'anime';
      });

      var all = movies.concat(tvShows);

      var heroPool = movies.filter(function (v) { return v.backdropPath && v.posterPath; });
      if (!heroPool.length) heroPool = all.filter(function (v) { return v.backdropPath; });
      var heroItem = heroPool[0] || all[0] || null;

      var html = heroItem ? buildHero(heroItem, 'mixed') : '';
      html += '<div class="am-genre-rows">';

      if (cw.length  > 0) html += buildGenreRow('Continue Watching', cw.slice(0, 20),  'mixed');
      if (movies.length  > 0) html += buildGenreRow('Recently Added Movies',   movies.slice(0, 24),  'movie');
      if (tvShows.length > 0) html += buildGenreRow('Recently Added TV Shows', tvShows.slice(0, 24), 'tv');
      if (anime.length   > 0) html += buildGenreRow('Recently Added Anime',    anime.slice(0, 24),   'anime');

      /* Top movie genre rows */
      var byGenre = groupByGenre(movies);
      Object.keys(byGenre).slice(0, 6).forEach(function (g) {
        var vids = byGenre[g];
        if (vids.length < 1) return;
        html += buildGenreRow(g, vids.slice(0, 24), 'movie');
      });

      html += '</div>';
      el.innerHTML = html;
      setupHeroCycle(heroPool);
    } catch (err) {
      console.error('[Amazing] home error:', err);
      el.innerHTML = '<div class="am-empty">Could not load home content.</div>';
    }
  }

  /* ════════════════════════════════════════════════
     6. HTML BUILDERS
  ════════════════════════════════════════════════ */
  function buildHero(v, mediaType) {
    var backdrop = v.backdropPath  ? '/videometa/'  + v.backdropPath
                 : v.thumbnailPath ? '/videothumb/' + v.thumbnailPath : '';
    var poster   = v.posterPath    ? '/videometa/'  + v.posterPath : '';
    var title    = esc(v.seriesName || v.title || '');
    var overview = v.overview ? esc(v.overview.substring(0, 220) + (v.overview.length > 220 ? '…' : '')) : '';
    var genre    = v.genre    ? '<span class="am-hero-genre">' + esc(v.genre.split(',')[0].trim()) + '</span>' : '';
    var rating   = v.rating > 0 ? '<span class="am-hero-rating">★ ' + v.rating.toFixed(1) + '</span>' : '';
    var year     = v.year    ? '<span class="am-hero-year">' + v.year + '</span>' : '';
    var playFn   = buildClickFn(v);

    return '<div class="am-hero" id="am-hero" style="' + (backdrop ? 'background-image:url(' + backdrop + ')' : '') + '">' +
      '<div class="am-hero-overlay"></div>' +
      '<button class="am-hero-nav am-hero-nav-left"  id="am-hero-prev" style="display:none" onclick="window._amHeroNav(-1)">' +
        '<span class="am-hero-nav-icon">&#10094;</span>' +
      '</button>' +
      '<button class="am-hero-nav am-hero-nav-right" id="am-hero-next" style="display:none" onclick="window._amHeroNav(1)">' +
        '<span class="am-hero-nav-icon">&#10095;</span>' +
      '</button>' +
      '<div class="am-hero-content">' +
        '<img class="am-hero-poster" id="am-hero-poster" src="' + (poster || '') + '" loading="lazy" alt=""' + (poster ? '' : ' style="display:none"') + '>' +
        '<div class="am-hero-text">' +
          '<div class="am-hero-meta" id="am-hero-meta">' + genre + rating + year + '</div>' +
          '<h1 class="am-hero-title" id="am-hero-title">' + title + '</h1>' +
          (overview
            ? '<p class="am-hero-overview" id="am-hero-overview">' + overview + '</p>'
            : '<p class="am-hero-overview" id="am-hero-overview" style="display:none"></p>') +
          '<div class="am-hero-btns">' +
            '<button class="am-hero-play" id="am-hero-play-btn" onclick="' + playFn + '">&#9654; Play</button>' +
            '<button class="am-hero-wl" id="am-hero-wl-btn" onclick="window._amWatchlist(this,' + v.id + ')">' +
              WL_ICON + 'Watchlist' +
            '</button>' +
            '<button class="am-hero-info" id="am-hero-info-btn" onclick="' + playFn + '">More Info</button>' +
          '</div>' +
        '</div>' +
      '</div>' +
      '<div class="am-hero-dots" id="am-hero-dots"></div>' +
    '</div>';
  }

  function buildGenreRow(genreName, videos, mediaType) {
    if (!videos || videos.length === 0) return '';
    var cards = '';
    for (var i = 0; i < videos.length; i++) { cards += buildCard(videos[i]); }
    var seeMorePage = (mediaType === 'movie') ? 'movies' : 'tvshows';
    return '<div class="am-row">' +
      '<div class="am-row-header">' +
        '<span class="am-row-title">' + esc(genreName) + '</span>' +
        '<a href="#" class="am-row-more" onclick="event.preventDefault();if(typeof App!==\'undefined\')App.navigate(\'' + seeMorePage + '\')">See more ›</a>' +
      '</div>' +
      '<div class="am-row-scroll">' + cards + '</div>' +
    '</div>';
  }

  function buildCard(v) {
    /* Poster art (portrait) → backdrop → FFmpeg thumbnail */
    var img = v.posterPath    ? '/videometa/'  + v.posterPath
            : v.backdropPath  ? '/videometa/'  + v.backdropPath
            : v.thumbnailPath ? '/videothumb/' + v.thumbnailPath
            : '';
    var title  = esc(v.seriesName || v.title || '');
    var rating = v.rating > 0 ? '<div class="am-card-rating">★ ' + v.rating.toFixed(1) + '</div>' : '';
    var click  = buildClickFn(v);
    return '<div class="am-card" onclick="' + click + '">' +
      '<div class="am-card-img">' +
        (img
          ? '<img src="' + img + '" loading="lazy" alt="" onerror="this.style.display=\'none\'">'
          : '<div class="am-card-noimg"></div>') +
        rating +
      '</div>' +
      '<div class="am-card-title">' + title + '</div>' +
    '</div>';
  }

  function buildClickFn(v) {
    if (v.seriesName) {
      var sn = v.seriesName.replace(/\\/g, '\\\\').replace(/'/g, "\\'");
      return 'App.openSeriesDetail(\'' + sn + '\'' + (v.mediaType === 'anime' ? ",'anime'" : '') + ')';
    }
    return 'App.openVideoDetail(' + v.id + ')';
  }

  /* ════════════════════════════════════════════════
     7. HERO CYCLE (auto-rotate every 8s) + ARROWS
  ════════════════════════════════════════════════ */
  function updateHeroDisplay(v) {
    var hero = document.getElementById('am-hero');
    if (!hero) return;
    /* Fade */
    hero.style.opacity = '.5';
    setTimeout(function () { if (document.getElementById('am-hero')) hero.style.opacity = '1'; }, 220);
    /* Background — fall back to FFmpeg thumbnail for docs without TMDB art */
    var bgSrc = v.backdropPath  ? '/videometa/'  + v.backdropPath
              : v.thumbnailPath ? '/videothumb/' + v.thumbnailPath : '';
    if (bgSrc) hero.style.backgroundImage = 'url(' + bgSrc + ')';
    /* Poster */
    var posterEl = document.getElementById('am-hero-poster');
    if (posterEl) {
      if (v.posterPath) { posterEl.src = '/videometa/' + v.posterPath; posterEl.style.display = ''; }
      else              { posterEl.style.display = 'none'; }
    }
    /* Title */
    var titleEl = document.getElementById('am-hero-title');
    if (titleEl) titleEl.textContent = v.seriesName || v.title || '';
    /* Overview */
    var overviewEl = document.getElementById('am-hero-overview');
    if (overviewEl) {
      overviewEl.textContent   = v.overview ? v.overview.substring(0, 220) + (v.overview.length > 220 ? '…' : '') : '';
      overviewEl.style.display = overviewEl.textContent ? '' : 'none';
    }
    /* Meta (genre / rating / year) */
    var metaEl = document.getElementById('am-hero-meta');
    if (metaEl) {
      var g = v.genre   ? '<span class="am-hero-genre">'  + esc(v.genre.split(',')[0].trim()) + '</span>' : '';
      var r = v.rating > 0 ? '<span class="am-hero-rating">★ ' + v.rating.toFixed(1) + '</span>' : '';
      var y = v.year    ? '<span class="am-hero-year">'   + v.year + '</span>' : '';
      metaEl.innerHTML = g + r + y;
    }
    /* Buttons */
    var clickFn = buildClickFn(v);
    var playBtn = document.getElementById('am-hero-play-btn');
    var infoBtn = document.getElementById('am-hero-info-btn');
    var wlBtn   = document.getElementById('am-hero-wl-btn');
    if (playBtn) playBtn.setAttribute('onclick', clickFn);
    if (infoBtn) infoBtn.setAttribute('onclick', clickFn);
    if (wlBtn) {
      wlBtn.setAttribute('onclick', 'window._amWatchlist(this,' + v.id + ')');
      _amWlSync(wlBtn, v.id);
    }
    /* Dots */
    document.querySelectorAll('.am-hero-dot').forEach(function (d, i) {
      d.classList.toggle('am-hero-dot-active', i === _heroCycleIdx);
    });
  }

  function _startCycleTimer() {
    _heroCycleTimer = setInterval(function () {
      if (!document.getElementById('am-hero')) { clearHeroCycle(); return; }
      _heroCycleIdx = (_heroCycleIdx + 1) % _heroCycleItems.length;
      updateHeroDisplay(_heroCycleItems[_heroCycleIdx]);
    }, 8000);
  }

  function setupHeroCycle(items) {
    clearHeroCycle();
    _heroCycleItems = items || [];
    _heroCycleIdx   = 0;
    var multi = _heroCycleItems.length > 1;
    /* Arrows */
    var prevBtn = document.getElementById('am-hero-prev');
    var nextBtn = document.getElementById('am-hero-next');
    if (prevBtn) prevBtn.style.display = multi ? '' : 'none';
    if (nextBtn) nextBtn.style.display = multi ? '' : 'none';
    /* Dots */
    var dotsEl = document.getElementById('am-hero-dots');
    if (dotsEl) {
      if (multi) {
        var dh = '';
        var dc = Math.min(_heroCycleItems.length, 10);
        for (var i = 0; i < dc; i++) {
          dh += '<button class="am-hero-dot' + (i === 0 ? ' am-hero-dot-active' : '') +
                '" onclick="window._amHeroNavTo(' + i + ')"></button>';
        }
        dotsEl.innerHTML = dh;
      } else {
        dotsEl.innerHTML = '';
      }
    }
    if (!multi) return;
    _startCycleTimer();
  }

  function clearHeroCycle() {
    clearInterval(_heroCycleTimer);
    _heroCycleTimer = null;
  }

  /* ── Watchlist button with inline feedback (no toast dependency) ── */
  var WL_ICON = '<svg width="15" height="15" style="vertical-align:-2px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-bookmark"/></svg> ';

  function _amWlLabel(videoId) {
    var inWl = App._watchlistIds && App._watchlistIds.has(videoId);
    return WL_ICON + (inWl ? '✓ In Watchlist' : 'Watchlist');
  }

  function _amWlSync(btn, videoId) {
    if (!btn) return;
    var inWl = App._watchlistIds && App._watchlistIds.has(videoId);
    btn.innerHTML = WL_ICON + (inWl ? '✓ In Watchlist' : 'Watchlist');
    btn.classList.toggle('am-hero-wl-active', !!inWl);
  }

  window._amWatchlist = function (btn, videoId) {
    if (!App || typeof App.toggleVideoWatchlist !== 'function') return;
    btn.disabled = true;
    /* Optimistic state toggle — reflect immediately, then confirm from server */
    var wasIn = App._watchlistIds && App._watchlistIds.has(videoId);

    /* Direct API call so we can control feedback ourselves */
    fetch('/api/videos/' + videoId + '/watchlist', { method: 'POST' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        if (!data) { btn.disabled = false; return; }
        if (data.inWatchlist) {
          if (App._watchlistIds) App._watchlistIds.add(videoId);
        } else {
          if (App._watchlistIds) App._watchlistIds.delete(videoId);
        }
        /* Show confirmation flash */
        btn.innerHTML = WL_ICON + (data.inWatchlist ? '✓ Added!' : '✓ Removed');
        btn.classList.toggle('am-hero-wl-active', !!data.inWatchlist);
        btn.classList.add('am-hero-wl-flash');
        setTimeout(function () {
          btn.classList.remove('am-hero-wl-flash');
          _amWlSync(btn, videoId);
          btn.disabled = false;
        }, 2000);
      })
      .catch(function () { btn.disabled = false; });
  };

  /* ── Hero navigation exposed for inline onclick ─────────────────── */
  window._amHeroNav = function (dir) {
    if (!_heroCycleItems.length) return;
    clearInterval(_heroCycleTimer);
    _heroCycleIdx = ((_heroCycleIdx + dir) + _heroCycleItems.length) % _heroCycleItems.length;
    updateHeroDisplay(_heroCycleItems[_heroCycleIdx]);
    _startCycleTimer();
  };

  window._amHeroNavTo = function (idx) {
    if (!_heroCycleItems.length) return;
    clearInterval(_heroCycleTimer);
    _heroCycleIdx = idx % _heroCycleItems.length;
    updateHeroDisplay(_heroCycleItems[_heroCycleIdx]);
    _startCycleTimer();
  };

  /* ════════════════════════════════════════════════
     8. DATA HELPERS
  ════════════════════════════════════════════════ */
  function extractVideos(data) {
    if (!data) return [];
    if (data.items)  return data.items;
    if (data.videos) return data.videos;
    if (Array.isArray(data)) return data;
    return [];
  }

  function extractGenres(data) {
    if (!data) return [];
    /* API returns [{name, count}] — extract the name strings */
    var arr = data.genres || (Array.isArray(data) ? data : []);
    return arr.map(function (g) { return typeof g === 'string' ? g : g.name || ''; })
              .filter(function (g) { return !!g; });
  }

  function groupByGenre(videos) {
    var result = {};
    videos.forEach(function (v) {
      if (!v.genre) return;
      /* Use only the FIRST genre so each title appears in exactly one row */
      var g = v.genre.split(',')[0].trim();
      if (!g) return;
      if (!result[g]) result[g] = [];
      result[g].push(v);
    });
    return result;
  }

  function esc(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  /* ════════════════════════════════════════════════
     9. PATCH App RENDER METHODS
  ════════════════════════════════════════════════ */
  function patchApp() {
    if (typeof App === 'undefined' || !App.navigate || App._amazingPatched) return;
    App._amazingPatched = true;

    /* Phase 1 must already have patched renderPage / renderHome etc.
       Here we only patch navigate (needs _initCfg for tabs) and sync state. */
    var _origNavigate = App.navigate.bind(App);

    App.navigate = function (page) {
      App._amVirtualPage = null;
      syncActive(page);
      clearHeroCycle();
      return _origNavigate(page);
    };

    /* Sync active tab */
    if (App.currentPage) syncActive(App.currentPage);

    /* Re-render current page with Amazing layout.
       The guard above ensures the in-flight original renderHome can't
       overwrite us, so this render will be the final one. */
    var cp = App.currentPage;
    if (cp === 'home' || cp === 'movies' || cp === 'tvshows' || cp === 'anime') {
      App.renderPage(cp);
    }
  }

  /* ════════════════════════════════════════════════
     10. BOOT — two-phase patch
         Phase 1: patch render methods the moment App
                  object exists — before the app calls
                  navigate('home') for the first time.
         Phase 2: build tabs / account / patch navigate
                  once _initCfg is available.
  ════════════════════════════════════════════════ */
  buildNav();

  var _tick        = 0;
  var _phase1Done  = false;
  var _phase2Done  = false;

  /* Guard #main-content against innerHTML overwrites by the original renderHome.
     The original renderHome() was already called before our script loaded — it is
     in-flight and will eventually do el.innerHTML = originalHtml, overwriting our
     Amazing layout.  We intercept innerHTML on the element instance and reject any
     content that doesn't belong to our Amazing layout while we own the page. */
  function guardMainContent() {
    var mc = document.getElementById('main-content');
    if (!mc || mc._amGuarded) return;
    mc._amGuarded = true;

    /* Walk the prototype chain to find the native innerHTML descriptor */
    var proto = mc, desc = null;
    while (proto) {
      desc = Object.getOwnPropertyDescriptor(proto, 'innerHTML');
      if (desc && desc.set) break;
      proto = Object.getPrototypeOf(proto);
    }
    if (!desc || !desc.set) return; /* can't intercept — skip guard */

    var nativeGet = desc.get;
    var nativeSet = desc.set;

    Object.defineProperty(mc, 'innerHTML', {
      configurable: true,
      get: function () { return nativeGet.call(mc); },
      set: function (val) {
        var cp = typeof App !== 'undefined' ? App.currentPage : null;
        var weOwnPage = (cp === 'home' || cp === 'movies' || cp === 'tvshows' || cp === 'anime');

        if (weOwnPage) {
          /* Block only the default grid renders from original renderHome/renderMovies/renderTvShows.
             Allow everything else through (detail views, Amazing content, loading states, etc.) */
          var isDefaultGrid = val.indexOf('class="home-section"')  !== -1
            || val.indexOf('class="filter-bar"')    !== -1;
          if (isDefaultGrid) return;
        }

        /* Track whether we're showing a detail view or Amazing! template content.
           Detail views get a padding wrapper class so all categories look consistent. */
        var isAmContent = val === ''
          || val.indexOf('am-loading')    !== -1
          || val.indexOf('am-hero')       !== -1
          || val.indexOf('am-genre-rows') !== -1;
        if (isAmContent) {
          mc.classList.remove('am-detail-view');
        } else {
          mc.classList.add('am-detail-view');
        }

        nativeSet.call(mc, val);
      }
    });
  }

  function phase1Patch() {
    if (_phase1Done) return;
    if (typeof App === 'undefined' || typeof App.renderHome !== 'function') return;
    _phase1Done = true;

    /* Guard main-content FIRST so the in-flight original renderHome can't win */
    guardMainContent();

    /* Also patch the render methods so future calls use our versions */
    App.renderHome = async function (el) {
      syncActive('home');
      await amazingRenderHome(el || document.getElementById('main-content'));
    };
    App.renderMovies = async function (el) {
      syncActive('movies');
      await amazingRender('movie', el || document.getElementById('main-content'));
    };
    App.renderTvShows = async function (el) {
      var isDoc = App._amVirtualPage === 'amdocs';
      syncActive(isDoc ? 'amdocs' : 'tvshows');
      await amazingRender(isDoc ? 'documentary' : 'tv', el || document.getElementById('main-content'));
    };
    App.renderAnime = async function (el) {
      syncActive('anime');
      await amazingRender('anime', el || document.getElementById('main-content'));
    };

    /* ── Search override: only show video-type results ── */
    App.performSearch = async function (query) {
      var el = document.getElementById('main-content');
      if (!el) return;
      clearHeroCycle();
      var titleEl = document.getElementById('page-title');
      if (titleEl) titleEl.innerHTML = '<span>Search: "' + esc(query) + '"</span>';
      el.innerHTML = '<div class="am-loading">Loading…</div>';

      var data = await App.api('search?q=' + encodeURIComponent(query));
      if (!data) { el.innerHTML = '<div class="am-empty">Search failed.</div>'; return; }

      /* Count only video-type results */
      var seen = new Set();
      var uniqueVids = (data.videos || []).filter(function (v) {
        if ((v.mediaType === 'tv' || v.mediaType === 'anime') && v.seriesName) {
          if (seen.has(v.seriesName)) return false;
          seen.add(v.seriesName);
        }
        return true;
      });
      var mvs   = data.musicVideos || [];
      var total = uniqueVids.length + mvs.length;

      var html = '<div class="am-genre-rows">'
        + '<div style="display:flex;align-items:baseline;gap:16px;margin-bottom:24px">'
        + '<h1 style="margin:0;font-size:24px;font-weight:700">Search Results</h1>'
        + '<span style="color:var(--text-secondary);font-size:13px">' + total + ' result' + (total !== 1 ? 's' : '') + '</span>'
        + '</div>';

      /* Music Videos row */
      if (mvs.length > 0) {
        html += '<div class="am-row"><div class="am-row-header"><span class="am-row-title">Music Videos</span></div>'
          + '<div class="am-row-scroll">';
        mvs.forEach(function (v) {
          var img = v.thumbnailPath ? '/mvthumb/' + v.thumbnailPath : '';
          html += '<div class="am-card" onclick="App.openMvDetail(' + v.id + ')" title="' + esc(v.title) + '">'
            + '<div class="am-card-img">'
            + (img ? '<img src="' + img + '" loading="lazy" onerror="this.style.display=\'none\'" alt="">' : '<div class="am-card-img-placeholder"></div>')
            + (v.year ? '<span class="am-card-rating">' + v.year + '</span>' : '')
            + '</div>'
            + '<div class="am-card-title">' + esc(v.title) + '</div>'
            + '<div class="am-card-sub">'  + esc(v.artist || '') + '</div>'
            + '</div>';
        });
        html += '</div></div>';
      }

      /* Videos row (movies / TV / docs / anime) */
      if (uniqueVids.length > 0) {
        html += '<div class="am-row"><div class="am-row-header"><span class="am-row-title">Movies &amp; TV Shows</span></div>'
          + '<div class="am-row-scroll">';
        uniqueVids.forEach(function (v) { html += buildCard(v); });
        html += '</div></div>';
      }

      if (total === 0) {
        html += '<div class="am-empty">No results found for &ldquo;' + esc(query) + '&rdquo;.</div>';
      }

      html += '</div>';
      el.innerHTML = html;
    };

    /* ── Music Videos override: simpler layout (no stats bar, no artist chips) ── */
    App.loadMvPage = async function (el) {
      var target = el || document.getElementById('main-content');
      if (!target) return;

      var url = 'musicvideos?limit=' + App.mvPerPage + '&page=' + App.mvPage + '&sort=' + App.mvSort;
      var data = await App.api(url);
      if (!data) { target.innerHTML = '<div class="am-empty">Could not load music videos.</div>'; return; }

      App.mvTotal = data.total;
      var totalPages = Math.ceil(data.total / App.mvPerPage);

      /* Sort chips — no stats bar, no artist chips */
      var sortMap = [
        ['recent',   App.t('sort.recent')],
        ['title',    App.t('sort.title')],
        ['artist',   App.t('sort.artist')],
        ['year',     App.t('sort.year')],
        ['size',     App.t('sort.size')],
        ['duration', App.t('sort.duration')]
      ];
      var sortChips = '';
      sortMap.forEach(function (pair) {
        sortChips += '<button class="filter-chip' + (App.mvSort === pair[0] ? ' active' : '') + '" onclick="App.changeMvSort(\'' + pair[0] + '\')">' + pair[1] + '</button>';
      });

      var html = '<div class="am-genre-rows">'
        + '<div class="page-header" style="margin-bottom:0">'
        + '<h1>' + App.t('page.musicVideos') + '</h1>'
        + '<div class="filter-bar">'
        + '<button class="mv-shuffle-btn" onclick="App.shuffleMusicVideo()" title="Play a random music video">'
        + '<svg><use href="#icon-shuffle"/></svg> ' + App.t('btn.shufflePlay')
        + '</button>'
        + sortChips
        + '</div></div>';

      if (data.videos && data.videos.length > 0) {
        html += '<div class="mv-grid" style="margin-top:24px">';
        data.videos.forEach(function (v) {
          var thumbSrc    = v.thumbnailPath ? '/mvthumb/' + v.thumbnailPath : '';
          var dur         = App.formatDuration(v.duration);
          var formatClass = v.mp4Compliant ? 'mv-format-ok' : (v.needsOptimization ? 'mv-format-warn' : 'mv-format-badge');
          var favClass    = v.isFavourite ? 'active' : '';
          html += '<div class="mv-card" onclick="App.openMvDetail(' + v.id + ')" data-mv-id="' + v.id + '">'
            + '<div class="mv-card-thumb">'
            + (thumbSrc
                ? '<img src="' + thumbSrc + '" loading="lazy" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\'" alt="">'
                + '<span class="mv-card-placeholder" style="display:none">&#127909;</span>'
                : '<span class="mv-card-placeholder">&#127909;</span>')
            + '<span class="mv-duration-badge">' + dur + '</span>'
            + '<span class="mv-format-badge ' + formatClass + '">' + esc(v.format) + '</span>'
            + '<button class="mv-card-play" onclick="event.stopPropagation();App.playMusicVideo(' + v.id + ')">&#9654;</button>'
            + '</div>'
            + '<div class="mv-card-info">'
            + '<div class="mv-card-title">' + esc(v.title)  + '</div>'
            + '<div class="mv-card-artist">' + esc(v.artist) + '</div>'
            + '</div>'
            + '<button class="song-card-fav ' + favClass + '" onclick="event.stopPropagation();App.toggleMvFav(' + v.id + ',this)">&#10084;</button>'
            + '</div>';
        });
        html += '</div>';
        html += App.renderMvPagination(App.mvPage, totalPages);
      } else {
        html += '<div class="am-empty" style="margin-top:40px">' + App.t('empty.noMusicVideos.title') + '</div>';
      }

      html += '</div>';
      target.innerHTML = html;
    };

    /* ── MV Detail: hide duration & size meta line below title ── */
    var _origOpenMvDetail = App.openMvDetail.bind(App);
    App.openMvDetail = async function (id) {
      await _origOpenMvDetail(id);
      /* The meta div has a unique inline style set in openMvDetail */
      var metaDiv = document.querySelector('#main-content .page-header div[style*="margin-top:4px"]');
      if (metaDiv) metaDiv.style.display = 'none';
    };

    var _origRenderPage = App.renderPage.bind(App);
    App.renderPage = async function (page) {
      var content = document.getElementById('main-content');
      if (!content) return _origRenderPage(page);
      content.innerHTML = '';
      if (page === 'home')    { await App.renderHome(content);    return; }
      if (page === 'movies')  { await App.renderMovies(content);  return; }
      if (page === 'tvshows') { await App.renderTvShows(content); return; }
      if (page === 'anime')   { await App.renderAnime(content);   return; }
      return _origRenderPage(page);
    };
  }

  var _iv = setInterval(function () {
    /* Phase 1 — as early as possible */
    if (!_phase1Done) phase1Patch();

    /* Phase 2 — needs _initCfg for tabs, account, navigate patch */
    if (!_phase2Done
        && typeof App !== 'undefined'
        && typeof App.navigate === 'function'
        && App._initCfg !== undefined) {

      _phase2Done = true;
      buildTabs();
      buildAccountDropdown();
      patchApp();
      clearInterval(_iv);
    }

    if (++_tick > 200) clearInterval(_iv);
  }, 50);

})();
