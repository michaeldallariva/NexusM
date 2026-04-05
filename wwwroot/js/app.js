// ═══════════════════════════════════════════════════════════
// NexusM - Client-Side Application
// Migrated from NexusM with full NexusM menu structure
// ═══════════════════════════════════════════════════════════

const App = {
    currentPage: 'home',
    audioPlayer: null,
    currentTrack: null,
    playlist: [],
    playIndex: -1,
    isPlaying: false,
    shuffle: false,
    repeat: 'off',
    sidebarCollapsed: false,
    musicPage: 1,
    musicSort: 'title',
    musicTotal: 0,
    musicPerPage: 100,
    musicSubView: 'all',
    musicFormat: '',
    _musicFormats: null,
    picturesPage: 1,
    picturesSort: 'recent',
    picturesCategory: null,
    picturesTotal: 0,
    picturesPerPage: 100,
    _pictureModalKeyHandler: null,
    ebooksPage: 1,
    ebooksSort: 'recent',
    ebooksCategory: null,
    ebooksTotal: 0,
    ebooksPerPage: 100,
    audioBooksPage: 1,
    audioBooksSort: 'recent',
    audioBooksCategory: null,
    audioBooksTotal: 0,
    audioBooksPerPage: 100,
    mvPage: 1,
    mvSort: 'recent',
    mvArtist: null,
    mvTotal: 0,
    mvPerPage: 60,
    _mvArtists: [],
    _mvArtistPage: 0,
    videosPage: 1,
    videosSort: 'recent',
    videosMediaType: null,
    videosGenre: null,
    videosCustomCategory: null,
    videosCustomGenreId: null,
    _cgVideoPage: 0,
    _cgMusicPage: 0,
    _cgEditId: null,
    _cgEditDomain: 'music',
    _cgEditData: null,
    videosTotal: 0,
    videosPerPage: 60,
    _homeRecentTracks: [],

    // New Releases state
    _nrCategory: 'movies',
    _nrCountry:  'US',
    _nrGenre:    '',
    _nrMode:     'recent',
    _nrPage:     1,

    // Radio state
    isRadioPlaying: false,
    currentRadioStation: null,
    radioStations: [],
    radioCountryFilter: null,
    radioGenreFilter: null,
    radioSearchFilter: '',
    _radioMetaInterval: null,
    _radioLastStreamTitle: null,

    // Resource metrics state
    _metricsInterval: null,

    // Podcast state
    podcastFeeds: [],
    podcastCurrentFeed: null,
    podcastEpisodes: [],
    podcastSearchFilter: '',

    // Equalizer state
    _audioCtx: null,
    _eqSource: null,
    _eqConnectedEl: null,
    _eqFilters: [],
    _eqEnabled: true,
    _eqBands: [0, 0, 0, 0, 0, 0, 0, 0],
    _eqPreset: 'Flat',
    _eqPanelOpen: false,
    _eqFreqs: [60, 170, 310, 600, 1000, 3000, 6000, 12000],
    _eqTypes: ['lowshelf', 'peaking', 'peaking', 'peaking', 'peaking', 'peaking', 'peaking', 'highshelf'],
    _eqPresets: {
        'Flat':       [0,  0,  0,  0,  0,  0,  0,  0],
        'Rock':       [5,  4,  0, -1,  0,  2,  3,  4],
        'Pop':        [2,  2,  1,  0, -1,  0,  2,  3],
        'Jazz':       [3,  2, -1,  0,  2,  2,  1,  0],
        'Classical':  [0,  0,  0,  0,  0,  0,  3,  5],
        'Electronic': [6,  4,  0,  0,  2,  3,  2,  3],
        'Bass Boost': [8,  6,  3,  0, -1, -1,  0,  0],
        'Vocal':      [-2,-1,  2,  3,  4,  3,  1, -1]
    },
    _eqMacros: {
        'Bass':     { bands: [0, 1],    coeff: [1.0, 0.7] },
        'Warmth':   { bands: [1, 2, 3], coeff: [0.5, 0.8, 0.5] },
        'Clarity':  { bands: [4, 5],    coeff: [0.4, 0.9] },
        'Presence': { bands: [5, 6],    coeff: [0.5, 0.9] },
        'Treble':   { bands: [6, 7],    coeff: [0.6, 1.0] }
    },

    // Go Big Mode
    _gbActive: false,
    _gbOverlay: null,
    _gbHeroTimer: null,
    _gbHeroItems: null,
    _gbHeroIdx: 0,
    _gbKeyHandler: null,
    _gbMovies: null,
    _gbTv: null,
    _gbDocos: null,
    _gbMvs: null,
    _gbTranscodeId: null,
    _gbFocusZone: 'hero',   // 'hero' | 'rows'
    _gbFocusRow: 0,
    _gbFocusCard: 0,
    _gbFocusHeroBtn: 0,
    _gbDetailFocusIdx: 0,
    _gbPollTimer: null,
    _gbFsMouseMove: null,
    _gbFsHoverTimer: null,
    _gbRemoteActive: false,
    _gbRemoteOverlay: null,
    _gbRemoteCheckTimer: null,
    _gbRemoteTab: 'movies',
    _gbRemoteVideos: null,
    _gbRemotePlaying: false,
    _gbRemoteSeriesView: false,
    _gbRemoteSeriesName: null,
    _gbPlaylists: null,
    _watchlistIds: new Set(),
    _gbRequestCheckTimer: null,
    _gbMobileStartBtn: null,

    // Night Club Mode
    _ncActive: false,
    _ncOverlay: null,
    _ncCanvas: null,
    _ncCtx: null,
    _ncAnimFrame: null,
    _ncAnalyser: null,
    _ncDataArray: null,
    _ncDancerFrame: 0,
    _ncDancerTimer: null,
    _ncKeyHandler: null,
    _ncResizeHandler: null,
    _ncVizStyle: 0,
    _ncVizLabelTimer: null,

    userRole: 'guest',
    userName: '',
    userDisplayName: '',
    childSettings: null,   // parsed object for child users; null for admin/guest

    // i18n
    _lang: {},
    _langCode: 'en',

    // ─── Init ────────────────────────────────────────────────
    async init() {
        // Check authentication before loading app
        try {
            const res = await fetch('/api/auth/session');
            if (res.ok) {
                const session = await res.json();
                if (session.securityByPin && !session.authenticated) {
                    window.location.href = '/login.html';
                    return;
                }
                if (session.authenticated) {
                    this.userRole = session.role || 'guest';
                    this.userName = session.username || '';
                    this.userDisplayName = session.displayName || '';
                    if (session.childSettings) {
                        try { this.childSettings = JSON.parse(session.childSettings); } catch(e) {}
                    }
                }
                this.securityByPin = !!session.securityByPin;
            } else if (res.status === 401) {
                window.location.href = '/login.html';
                return;
            }
        } catch (e) { /* continue loading */ }

        this.audioPlayer = document.getElementById('audio-player');
        this.loadEQState();
        this.bindNavigation();
        this.bindPlayer();
        this.bindSearch();
        this.bindToolbar();
        this.bindSidebar();
        this.bindMobileNav();
        this.loadSidebarState();
        this.applyMenuVisibility();
        this.applyRoleVisibility();
        if (this.securityByPin) {
            const logoutBtn = document.getElementById('btn-logout');
            if (logoutBtn) logoutBtn.style.display = '';
        }
        this.loadBadgeCounts();

        // Load saved language + Go Big autorun setting
        let _initCfg = null;
        try {
            const cfgRes = await fetch('/api/config/info');
            if (cfgRes.ok) {
                _initCfg = await cfgRes.json();
                this._initCfg = _initCfg;
                this.applyTheme(_initCfg.theme || 'dark');
                this.applyTemplate(_initCfg.uiTemplate || '');
                await this.loadLanguage(_initCfg.language || 'en');
            }
        } catch (e) { /* default to en */ }

        // Stop any active transcode when the browser tab is closed or refreshed
        window.addEventListener('beforeunload', () => {
            if (this._currentTranscodeId) {
                navigator.sendBeacon(`/api/stop-transcode/${this._currentTranscodeId}`, '');
            }
        });

        this.navigate('home');
        this.checkWelcomeModal();
        if (sessionStorage.getItem('nexusm-gobig') || _initCfg?.goBigDefault) this.startGoBigMode();
        else if (this._gbIsMobile()) this._gbStartRemoteCheck();
        else this._gbStartRequestCheck();
    },

    // ─── i18n ───────────────────────────────────────────────
    t(key, fallback) {
        return this._lang[key] || fallback || key;
    },

    async loadLanguage(code) {
        const v = '?v=3';
        try {
            const res = await fetch(`/lang/${code}.json${v}`, { cache: 'no-store' });
            if (res.ok) {
                this._lang = await res.json();
                this._langCode = code;
            } else {
                console.warn(`Language file ${code}.json not found, falling back to en`);
                if (code !== 'en') {
                    const enRes = await fetch(`/lang/en.json${v}`, { cache: 'no-store' });
                    if (enRes.ok) { this._lang = await enRes.json(); this._langCode = 'en'; }
                }
            }
        } catch (e) {
            console.error('Failed to load language:', e);
        }
        this.applyLanguageToSidebar();
    },

    applyLanguageToSidebar() {
        const map = {
            'home': 'nav.home', 'movies': 'nav.movies', 'tvshows': 'nav.tvShows', 'music': 'nav.music',
            'musicvideos': 'nav.musicVideos', 'radio': 'nav.radio', 'internettv': 'nav.internetTv', 'podcasts': 'nav.podcasts',
            'anime': 'nav.anime',
            'pictures': 'nav.pictures', 'ebooks': 'nav.ebooks', 'favourites': 'nav.favourites',
            'playlists': 'nav.playlists', 'watchlist': 'nav.watchlist', 'mostplayed': 'nav.mostPlayed', 'analysis': 'nav.analysis',
            'settings': 'nav.settings', 'rescan': 'nav.rescanFolders'
        };
        document.querySelectorAll('.sidebar-nav a[data-page]').forEach(a => {
            const page = a.getAttribute('data-page');
            if (map[page]) {
                const label = a.querySelector('.nav-label:not(.nav-badge)');
                if (label) label.textContent = this.t(map[page]);
            }
        });
        // Section titles
        const sectionMap = { 'Main': 'nav.main', 'Media': 'nav.media', 'Live': 'nav.live',
            'Library': 'nav.library', 'Collections': 'nav.collections', 'Admin': 'nav.admin' };
        document.querySelectorAll('.sidebar-section-title .nav-label').forEach(el => {
            const key = sectionMap[el.textContent.trim()] || sectionMap[el.getAttribute('data-i18n-original')];
            if (key) {
                if (!el.getAttribute('data-i18n-original')) el.setAttribute('data-i18n-original', el.textContent.trim());
                el.textContent = this.t(key);
            }
        });
        // Status section
        const statusLabels = document.querySelectorAll('.sidebar-status .status-label');
        statusLabels.forEach(el => {
            const orig = el.getAttribute('data-i18n-original') || el.textContent.trim();
            if (!el.getAttribute('data-i18n-original')) el.setAttribute('data-i18n-original', orig);
            if (orig === 'Status') el.textContent = this.t('nav.status');
            else if (orig === 'Storage used') el.textContent = this.t('nav.storageUsed');
        });
        const sharesTitle = document.querySelector('.status-shares-title');
        if (sharesTitle) sharesTitle.textContent = this.t('nav.mediaShares');
        // Sidebar toggle buttons
        const collapseBtn = document.getElementById('sidebar-toggle');
        if (collapseBtn) collapseBtn.title = this.t('nav.collapseSidebar');
        const expandBtn = document.getElementById('sidebar-expand-btn');
        if (expandBtn) expandBtn.title = this.t('nav.expandSidebar');
    },

    applyTheme(theme) {
        document.body.classList.remove('theme-blue', 'theme-purple', 'theme-emerald', 'theme-sky-grey', 'theme-sky-blue');
        if (theme === 'blue') document.body.classList.add('theme-blue');
        else if (theme === 'purple') document.body.classList.add('theme-purple');
        else if (theme === 'emerald') document.body.classList.add('theme-emerald');
        else if (theme === 'sky-grey') document.body.classList.add('theme-sky-grey');
        else if (theme === 'sky-blue') document.body.classList.add('theme-sky-blue');
        try { localStorage.setItem('nexusm-theme', theme); } catch(e) {}
    },

    applyTemplate(templateId) {
        const cssLink = document.getElementById('tpl-css');
        if (cssLink) cssLink.href = templateId ? `/templates/${templateId}/style.css` : '';
        // Remove any previously injected template script
        document.querySelectorAll('script[data-tpl-script]').forEach(s => s.remove());
        if (templateId) {
            const s = document.createElement('script');
            s.src = `/templates/${templateId}/script.js`;
            s.setAttribute('data-tpl-script', templateId);
            document.body.appendChild(s);
        }
        try { localStorage.setItem('nexusm-template', templateId || ''); } catch(e) {}
        this._activeTemplate = templateId || '';
    },

    async applyMenuVisibility() {
        const config = await this.api('config/info');
        if (!config) return;
        const cs = this.childSettings;  // non-null for child/guest with saved settings
        const isChild = this.userRole === 'child';
        // For child users with no saved settings every section defaults to visible (matching
        // the "all checked" default shown in the admin UI). For non-child users fall back to
        // the global config as before.
        const csOrDefault = (key, configKey) => cs
            ? (cs[key] ?? true)
            : (isChild ? true : config[configKey]);
        const toggles = {
            music:       csOrDefault('showMusic',       'showMusic'),
            pictures:    csOrDefault('showPictures',    'showPictures'),
            movies:      cs ? (cs['showMovies']   ?? cs['showMoviesTV'] ?? true) : (isChild ? true : config['showMovies']),
            tvshows:     cs ? (cs['showTvShows']  ?? cs['showMoviesTV'] ?? true) : (isChild ? true : config['showTvShows']),
            musicvideos: csOrDefault('showMusicVideos', 'showMusicVideos'),
            radio:       csOrDefault('showRadio',       'showRadio'),
            internettv:  csOrDefault('showInternetTV',  'showInternetTV'),
            podcasts:    csOrDefault('showPodcasts',    'showPodcasts'),
            anime:       csOrDefault('showAnime',       'showAnime'),
            ebooks:      csOrDefault('showEBooks',      'showEBooks'),
            audiobooks:  csOrDefault('showAudioBooks',  'showAudioBooks'),
            actors:      isChild ? false : config.showActors  // always hidden for child users
        };

        // ── Demo mode: server-authoritative overrides ──────────────────────────
        // Values come from the server's config/info response (parsed from NexusM.conf
        // at startup). The client cannot bypass this by manipulating HTML/JS.
        const dm = config.demoModeEnabled ? config.demoMode : null;
        if (dm) {
            if (!dm.showMusic)      toggles.music       = false;
            if (!dm.showPicture)    toggles.pictures    = false;
            if (!dm.showMusicVideo) toggles.musicvideos = false;
            if (!dm.showRadio)      toggles.radio       = false;
            if (!dm.showTV)         toggles.internettv  = false;
            if (!dm.showPodcast)    toggles.podcasts    = false;
            if (!dm.showEBooks)     toggles.ebooks      = false;
            if (!dm.showAudioBooks) toggles.audiobooks  = false;
        }
        // ──────────────────────────────────────────────────────────────────────

        // Cache for renderHome — so the home page mirrors the same visibility rules
        this._pageToggles = toggles;

        Object.entries(toggles).forEach(([page, visible]) => {
            // Sidebar links
            const link = document.querySelector(`a[data-page="${page}"]`);
            if (link) link.closest('li').style.display = visible ? '' : 'none';
            // Mobile Library sub-menu buttons
            const btn = document.querySelector(`.mobile-library-menu button[data-page="${page}"]`);
            if (btn) btn.style.display = visible ? '' : 'none';
        });

        // Demo mode: hide Settings (and the whole Admin sidebar section) if SETTINGS=FALSE
        if (dm && !dm.showSettings) {
            ['settings', 'analysis', 'rescan', 'insights'].forEach(page => {
                const link = document.querySelector(`a[data-page="${page}"]`);
                if (link) link.closest('li').style.display = 'none';
            });
            const adminSection = document.querySelector(`a[data-page="analysis"]`)?.closest('.sidebar-section');
            if (adminSection) adminSection.style.display = 'none';
        }

        // Demo mode badge
        const badge = document.getElementById('demo-mode-badge');
        if (badge) badge.style.display = config.demoModeEnabled ? 'block' : 'none';
    },

    applyRoleVisibility() {
        // Hide admin-only pages for non-admin users
        const adminPages = ['analysis', 'settings', 'rescan'];
        if (this.userRole !== 'admin') {
            adminPages.forEach(page => {
                const link = document.querySelector(`a[data-page="${page}"]`);
                if (link) link.closest('li').style.display = 'none';
            });
            // Hide the entire Admin section header
            const adminSection = document.querySelector(`a[data-page="analysis"]`)?.closest('.sidebar-section');
            if (adminSection) adminSection.style.display = 'none';
        }
        // Additional restrictions for child users
        if (this.userRole === 'child') {
            ['insights', 'bestrated', 'actors'].forEach(page => {
                const link = document.querySelector(`a[data-page="${page}"]`);
                if (link) link.closest('li').style.display = 'none';
            });
        }
    },

    async loadBadgeCounts() {
        const stats = await this.api('stats');
        if (stats) {
            const musicBadge = document.getElementById('badge-music');
            if (musicBadge) musicBadge.textContent = stats.totalTracks || 0;
        }
        const picStats = await this.api('pictures/stats');
        if (picStats) {
            const picBadge = document.getElementById('badge-pictures');
            if (picBadge) picBadge.textContent = picStats.totalPictures || 0;
        }
        const ebookStats = await this.api('ebooks/stats');
        if (ebookStats) {
            const ebookBadge = document.getElementById('badge-ebooks');
            if (ebookBadge) ebookBadge.textContent = ebookStats.totalEBooks || 0;
        }
        const audioBookStats = await this.api('audiobooks/stats');
        if (audioBookStats) {
            const abBadge = document.getElementById('badge-audiobooks');
            if (abBadge) abBadge.textContent = audioBookStats.totalAudioBooks || 0;
        }
        const mvStats = await this.api('musicvideos/stats');
        if (mvStats) {
            const mvBadge = document.getElementById('badge-musicvideos');
            if (mvBadge) mvBadge.textContent = mvStats.totalVideos || 0;
        }
        const videoStats = await this.api('videos/stats');
        if (videoStats) {
            const moviesBadge = document.getElementById('badge-movies');
            if (moviesBadge) moviesBadge.textContent = videoStats.totalMovies || 0;
            const tvBadge = document.getElementById('badge-tvshows');
            if (tvBadge) tvBadge.textContent = (videoStats.totalTvSeries || 0) + (videoStats.totalDocumentaries || 0);
            const animeBadge = document.getElementById('badge-anime');
            if (animeBadge) animeBadge.textContent = videoStats.totalAnime || 0;
        }
        const podcastStats = await this.api('podcasts/stats');
        if (podcastStats) {
            const podcastBadge = document.getElementById('badge-podcasts');
            if (podcastBadge) podcastBadge.textContent = podcastStats.totalFeeds || 0;
        }
        const actorStats = await this.api('actors/stats');
        if (actorStats) {
            const actorBadge = document.getElementById('badge-actors');
            if (actorBadge) actorBadge.textContent = actorStats.total || 0;
        }
        this.loadSharesStatus();
    },

    async loadSharesStatus() {
        const data = await this.api('status/shares');
        if (!data) return;

        // Total storage
        const storageEl = document.getElementById('status-storage');
        if (storageEl) storageEl.textContent = this.formatSize(data.totalSize);

        // Per-type sizes
        const sizesEl = document.getElementById('status-sizes');
        if (sizesEl && data.shares) {
            let html = '';
            data.shares.forEach(s => {
                if (s.configured && s.size > 0) {
                    html += `<div class="status-size-row"><span class="status-size-label">${s.type}</span><span class="status-size-value">${this.formatSize(s.size)}</span></div>`;
                }
            });
            sizesEl.innerHTML = html;
        }

        // Cached Medias section
        const cacheEl = document.getElementById('status-cache');
        if (cacheEl && data.cache) {
            const c = data.cache;
            let html = '';

            // HLS cache row: "X.X GB / 100 GB"
            const hlsSizeStr = this.formatSize(c.hlsSize);
            const hlsMaxStr = c.hlsMaxGB > 0 ? `/ ${c.hlsMaxGB} GB` : '';
            html += `<div class="status-size-row"><span class="status-size-label">HLS Cache</span><span class="status-size-value">${hlsSizeStr} <span style="color:var(--text-muted);font-weight:normal;font-size:10px">${hlsMaxStr}</span></span></div>`;

            // Days until oldest entry is eligible for purge
            if (c.hlsOldestEntryAgeDays >= 0) {
                const daysLeft = c.hlsRetentionDays - c.hlsOldestEntryAgeDays;
                const purgeStr = daysLeft > 0 ? `oldest entry expires in ${daysLeft}d` : 'oldest entry eligible for purge';
                html += `<div class="status-size-row"><span class="status-size-label" style="padding-left:8px;font-size:10px;line-height:1.4">↳ ${purgeStr}</span></div>`;
            } else {
                html += `<div class="status-size-row"><span class="status-size-label" style="padding-left:8px;font-size:10px;line-height:1.4">↳ recycles after ${c.hlsRetentionDays} days</span></div>`;
            }

            // Remux cache row (only if non-zero)
            if (c.remuxSize > 0) {
                html += `<div class="status-size-row" style="margin-top:2px"><span class="status-size-label">Remux Cache</span><span class="status-size-value">${this.formatSize(c.remuxSize)}</span></div>`;
            }

            cacheEl.innerHTML = html;
        }

        // Media shares status
        const sharesEl = document.getElementById('status-shares');
        if (sharesEl && data.shares) {
            let html = '';
            data.shares.forEach(s => {
                if (s.configured) {
                    const cls = s.active ? 'status-share-online' : 'status-share-offline';
                    const label = s.active ? 'ONLINE' : 'OFFLINE';
                    html += `<div class="status-share-row"><span class="status-share-label">${s.type}</span><span class="${cls}">${label}</span></div>`;
                }
            });
            if (!html) {
                html = '<div style="color:var(--text-muted);font-size:11px">No shares configured</div>';
            }
            sharesEl.innerHTML = html;
        }
    },

    // ─── Sidebar Collapse / Expand ───────────────────────────
    bindSidebar() {
        document.getElementById('sidebar-toggle').addEventListener('click', () => this.toggleSidebar());
        document.getElementById('sidebar-expand-btn').addEventListener('click', () => {
            // On mobile, open drawer instead of desktop collapse toggle
            if (this._isMobile()) return; // handled by bindMobileNav
            this.toggleSidebar();
        });
    },

    toggleSidebar() {
        // On mobile, toggle drawer instead
        if (this._isMobile()) {
            const sidebar = document.getElementById('sidebar');
            if (sidebar && sidebar.classList.contains('mobile-open')) {
                this.closeMobileDrawer();
            } else {
                this.openMobileDrawer();
            }
            return;
        }
        this.sidebarCollapsed = !this.sidebarCollapsed;
        const container = document.querySelector('.app-container');
        const expandBtn = document.getElementById('sidebar-expand-btn');
        const toggleBtn = document.getElementById('sidebar-toggle');

        if (this.sidebarCollapsed) {
            container.classList.add('sidebar-collapsed');
            expandBtn.style.display = 'block';
            toggleBtn.innerHTML = '&raquo;';
        } else {
            container.classList.remove('sidebar-collapsed');
            expandBtn.style.display = 'none';
            toggleBtn.innerHTML = '&laquo;';
        }
        localStorage.setItem('nexusm-sidebar', this.sidebarCollapsed ? 'collapsed' : 'expanded');
    },

    loadSidebarState() {
        const saved = localStorage.getItem('nexusm-sidebar');
        if (saved === 'collapsed') {
            this.sidebarCollapsed = true;
            document.querySelector('.app-container').classList.add('sidebar-collapsed');
            document.getElementById('sidebar-expand-btn').style.display = 'block';
            document.getElementById('sidebar-toggle').innerHTML = '&raquo;';
        }
    },

    // ─── Mobile Navigation ───────────────────────────────────
    _isMobile() {
        return window.innerWidth <= 768;
    },

    bindMobileNav() {
        const mobileNav = document.getElementById('mobile-nav');
        if (!mobileNav) return;

        // Bottom nav tab clicks
        mobileNav.querySelectorAll('.mobile-nav-item[data-page]').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.preventDefault();
                const page = btn.dataset.page;
                // Library tab shows sub-menu popup
                if (page === 'library') {
                    this.toggleMobileLibraryMenu();
                    return;
                }
                this.closeMobileLibraryMenu();
                this.navigate(page);
            });
        });

        // Library sub-menu item clicks
        const libMenu = document.getElementById('mobile-library-menu');
        if (libMenu) {
            libMenu.querySelectorAll('button[data-page]').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.preventDefault();
                    this.closeMobileLibraryMenu();
                    this.navigate(btn.dataset.page);
                });
            });
        }

        // Sidebar overlay click to close drawer
        const overlay = document.getElementById('sidebar-overlay');
        if (overlay) {
            overlay.addEventListener('click', () => this.closeMobileDrawer());
        }

        // Close sidebar drawer when a sidebar nav link is clicked on mobile
        document.querySelectorAll('.sidebar-nav a[data-page]').forEach(link => {
            link.addEventListener('click', () => {
                if (this._isMobile()) this.closeMobileDrawer();
            });
        });

        // Hamburger button on mobile opens the drawer
        document.getElementById('sidebar-expand-btn').addEventListener('click', (e) => {
            if (this._isMobile()) {
                e.stopPropagation();
                this.openMobileDrawer();
            }
        });

        // Mobile search: tap search icon to toggle input
        const searchIcon = document.querySelector('.search-box .search-icon');
        const searchInput = document.getElementById('global-search');
        if (searchIcon && searchInput) {
            searchIcon.addEventListener('click', (e) => {
                if (this._isMobile()) {
                    e.stopPropagation();
                    searchInput.classList.toggle('mobile-search-open');
                    if (searchInput.classList.contains('mobile-search-open')) {
                        searchInput.focus();
                    }
                }
            });
            // Close search on blur when empty
            searchInput.addEventListener('blur', () => {
                if (this._isMobile() && !searchInput.value) {
                    searchInput.classList.remove('mobile-search-open');
                }
            });
        }

        // Close library menu when clicking outside
        document.addEventListener('click', (e) => {
            const libMenu = document.getElementById('mobile-library-menu');
            const libBtn = document.getElementById('mobile-nav-library');
            if (libMenu && libMenu.classList.contains('open') &&
                !libMenu.contains(e.target) && !libBtn.contains(e.target)) {
                this.closeMobileLibraryMenu();
            }
        });

        // Toggle mobile-player-active class on body when player shows/hides
        this._observePlayerVisibility();
    },

    openMobileDrawer() {
        const sidebar = document.getElementById('sidebar');
        const overlay = document.getElementById('sidebar-overlay');
        if (sidebar) sidebar.classList.add('mobile-open');
        if (overlay) overlay.classList.add('active');
    },

    closeMobileDrawer() {
        const sidebar = document.getElementById('sidebar');
        const overlay = document.getElementById('sidebar-overlay');
        if (sidebar) sidebar.classList.remove('mobile-open');
        if (overlay) overlay.classList.remove('active');
    },

    toggleMobileLibraryMenu() {
        const menu = document.getElementById('mobile-library-menu');
        if (menu) menu.classList.toggle('open');
    },

    closeMobileLibraryMenu() {
        const menu = document.getElementById('mobile-library-menu');
        if (menu) menu.classList.remove('open');
    },

    updateMobileNavActive(page) {
        const mobileNav = document.getElementById('mobile-nav');
        if (!mobileNav) return;
        // Map pages to their bottom nav tab
        const tabMap = { home: 'home', movies: 'movies', music: 'music', settings: 'settings' };
        // Sub-pages that belong to Library
        const libraryPages = ['pictures', 'ebooks', 'musicvideos', 'radio', 'internettv', 'podcasts', 'favourites', 'playlists', 'watchlist', 'analysis'];
        let activeTab = tabMap[page] || (libraryPages.includes(page) ? 'library' : null);
        // Admin pages map to More
        if (['rescan'].includes(page)) activeTab = 'settings';

        mobileNav.querySelectorAll('.mobile-nav-item').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.page === activeTab);
        });
    },

    _observePlayerVisibility() {
        // Use MutationObserver to watch player-bar class changes
        const playerBar = document.getElementById('player-bar');
        if (!playerBar) return;
        const observer = new MutationObserver(() => {
            const hidden = playerBar.classList.contains('player-hidden');
            document.body.classList.toggle('mobile-player-active', !hidden);
        });
        observer.observe(playerBar, { attributes: true, attributeFilter: ['class'] });
    },

    // ─── API Helpers ─────────────────────────────────────────
    async api(endpoint) {
        try {
            const res = await fetch(`/api/${endpoint}`);
            if (res.status === 401) { window.location.href = '/login.html'; return null; }
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            return await res.json();
        } catch (err) {
            console.error(`API error [${endpoint}]:`, err);
            return null;
        }
    },

    async apiPost(endpoint, body = {}) {
        try {
            const res = await fetch(`/api/${endpoint}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
            if (res.status === 401) { window.location.href = '/login.html'; return null; }
            return await res.json();
        } catch (err) {
            console.error(`API POST error [${endpoint}]:`, err);
            return null;
        }
    },

    async apiPut(endpoint, body = {}) {
        try {
            const res = await fetch(`/api/${endpoint}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
            if (res.status === 401) { window.location.href = '/login.html'; return null; }
            return await res.json();
        } catch (err) {
            console.error(`API PUT error [${endpoint}]:`, err);
            return null;
        }
    },

    async apiDelete(endpoint) {
        try {
            const res = await fetch(`/api/${endpoint}`, { method: 'DELETE' });
            if (res.status === 401) { window.location.href = '/login.html'; return null; }
            return await res.json();
        } catch (err) {
            console.error(`API DELETE error [${endpoint}]:`, err);
            return null;
        }
    },

    // ─── Navigation ──────────────────────────────────────────
    bindNavigation() {
        document.querySelectorAll('.sidebar-nav a[data-page]').forEach(link => {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                this.navigate(link.dataset.page);
            });
        });
    },

    // ─── Shared Video Streaming ────────────────────────────────────
    // Unified playback for any video element: handles HLS (.m3u8),
    // future transcoded streams, and direct file URLs.
    // Use this for TV channels, Movies, TV Shows, etc.

    playVideoStream(videoEl, url, onFatalMediaError) {
        this.stopVideoStream(videoEl);
        if (url.includes('.m3u8') || url.includes('/hls/')) {
            if (typeof Hls !== 'undefined' && Hls.isSupported()) {
                const hls = new Hls({ enableWorker: true });
                videoEl._hlsInstance = hls;
                hls.loadSource(url);
                hls.attachMedia(videoEl);
                hls.on(Hls.Events.MANIFEST_PARSED, () => { videoEl.play(); });
                let mediaErrorCount = 0;
                hls.on(Hls.Events.ERROR, (event, data) => {
                    if (data.fatal) {
                        console.error('HLS fatal error:', data);
                        if (data.type === Hls.ErrorTypes.NETWORK_ERROR) {
                            hls.startLoad();
                        } else if (data.type === Hls.ErrorTypes.MEDIA_ERROR) {
                            mediaErrorCount++;
                            if (mediaErrorCount === 1) {
                                hls.recoverMediaError();
                            } else {
                                // Recovery failed — codec likely unsupported
                                hls.destroy();
                                videoEl._hlsInstance = null;
                                if (onFatalMediaError) onFatalMediaError();
                            }
                        } else {
                            hls.destroy();
                            videoEl._hlsInstance = null;
                            if (onFatalMediaError) onFatalMediaError();
                        }
                    }
                });
            } else if (videoEl.canPlayType('application/vnd.apple.mpegurl')) {
                videoEl.src = url;
                videoEl.play();
            }
        } else {
            videoEl.src = url;
            videoEl.play();
        }
    },

    stopVideoStream(videoEl) {
        if (!videoEl) return;
        if (videoEl._hlsInstance) {
            videoEl._hlsInstance.destroy();
            videoEl._hlsInstance = null;
        }
        videoEl.pause();
        videoEl.removeAttribute('src');
        videoEl.load();
    },

    navigate(page) {
        // Block admin-only pages for guests and children
        const adminOnlyPages = ['analysis', 'settings', 'rescan'];
        if (this.userRole !== 'admin' && adminOnlyPages.includes(page)) {
            page = 'home';
        }
        // Additional pages blocked for child users
        if (this.userRole === 'child' && ['insights', 'bestrated', 'actors'].includes(page)) {
            page = 'home';
        }
        // Stop metrics polling when leaving settings page
        if (this.currentPage === 'settings') this._stopMetricsPolling();
        // Exit batch select mode when navigating away from videos
        if (this.currentPage === 'videos' && page !== 'videos') { this._batchSelectMode = false; this._batchSelectedIds = null; }
        this.currentPage = page;
        this._mvShuffleMode = false;
        if (this.isRadioPlaying) {
            this.stopRadio();
        }
        // Close EQ panel on page navigation
        if (this._eqPanelOpen) {
            const panel = document.getElementById('eq-panel');
            if (panel) panel.style.display = 'none';
            this._eqPanelOpen = false;
            document.getElementById('btn-eq').classList.remove('eq-active');
        }
        // Stop any video elements, music player, TV player, lyrics overlay
        this.closeTvPlayer();
        const lyricsOv = document.getElementById('lyrics-overlay');
        if (lyricsOv) { lyricsOv.style.display = 'none'; document.getElementById('btn-player-lyrics').style.color = ''; }
        document.querySelectorAll('video').forEach(v => this.stopVideoStream(v));
        this.stopCurrentTranscode();
        this.stopPlayer();
        document.querySelectorAll('.sidebar-nav a').forEach(a => a.classList.remove('active'));
        const activeLink = document.querySelector(`a[data-page="${page}"]`);
        if (activeLink) activeLink.classList.add('active');

        const titles = {
            home: 'Home',
            movies: 'Movies', tvshows: 'TV Shows/Docs', music: 'Music', musicvideos: 'Music Videos',
            radio: 'Radio', internettv: 'Internet TV', podcasts: 'Podcasts',
            albums: 'Albums', artists: 'Artists', songs: 'Songs', genres: 'Genres',
            pictures: 'Pictures', ebooks: 'eBooks',
            favourites: 'Favorites', playlists: 'Playlists', watchlist: 'Watchlist',
            recent: 'Recently Added', mostplayed: 'Most Played', bestrated: 'Best Rated',
            analysis: 'Analysis', insights: 'Smart Insights', settings: 'Settings',
            rescan: 'Rescan Folders'
        };
        document.getElementById('page-title').innerHTML = `<span>${titles[page] || page}</span>`;
        this.updateMobileNavActive(page);
        this.renderPage(page);
    },

    async renderPage(page) {
        const content = document.getElementById('main-content');
        content.innerHTML = '<div class="spinner"></div>';

        switch (page) {
            // Existing functional pages
            case 'home':       await this.renderHome(content); break;
            case 'albums':     await this.renderAlbums(content); break;
            case 'artists':    await this.renderArtists(content); break;
            case 'songs':      await this.renderSongs(content); break;
            case 'genres':     await this.renderGenres(content); break;
            case 'favourites': await this.renderFavourites(content); break;
            case 'playlists':  await this.renderPlaylists(content); break;
            case 'watchlist':  await this.renderWatchlist(content); break;
            case 'recent':     await this.renderRecent(content); break;
            case 'mostplayed': await this.renderMostPlayed(content); break;
            case 'bestrated':  await this.renderBestRated(content); break;
            case 'settings':   await this.renderSettings(content); break;
            case 'rescan':     await this.renderRescan(content); break;

            // New NexusM pages — placeholder UI for now
            case 'movies':      await this.renderMovies(content); break;
            case 'tvshows':     await this.renderTvShows(content); break;
            case 'music':       await this.renderMusic(content); break;
            case 'musicvideos': await this.renderMusicVideos(content); break;
            case 'radio':       await this.renderRadio(content); break;
            case 'internettv':  await this.renderInternetTv(content); break;
            case 'podcasts':    await this.renderPodcasts(content); break;
            case 'pictures':    await this.renderPictures(content); break;
            case 'ebooks':      await this.renderEBooks(content); break;
            case 'audiobooks':  await this.renderAudioBooks(content); break;
            case 'anime':       await this.renderAnime(content); break;
            case 'analysis':    await this.renderAnalysis(content); break;
            case 'insights':     await this.renderInsights(content); break;
            case 'newreleases':   await this.renderNewReleases(content); break;
            case 'wheretowatch':  await this.renderWhereToWatch(content); break;
            case 'actors':        await this.renderActors(content); break;

            default: content.innerHTML = '<div class="empty-state"><h2>Page not found</h2></div>';
        }
    },

    // ─── Placeholder Page (for menus not yet built) ──────────
    renderPlaceholder(el, icon, title, description, tag) {
        el.innerHTML = `<div class="placeholder-page">
            <div class="ph-icon">${icon}</div>
            <h2>${title}</h2>
            <p>${description}</p>
            <div class="ph-tag">${tag}</div>
        </div>`;
    },

    // ─── Radio Page ────────────────────────────────────────────
    async renderRadio(el) {
        const data = await this.api('radio/stations');
        if (!data || !data.stations || data.stations.length === 0) {
            el.innerHTML = this.emptyState(this.t('empty.noRadio.title'), this.t('empty.noRadio.desc'));
            return;
        }

        this.radioStations = data.stations;
        this.radioCountryFilter = null;
        this.radioGenreFilter = null;
        this.radioSearchFilter = '';

        let html = '<div class="radio-header">';
        html += '<div class="radio-toolbar">';
        html += `<input type="text" id="radio-search" class="radio-search-input" placeholder="${this.t('search.stationsPlaceholder')}" oninput="App.filterRadioStations()">`;
        html += `<button class="btn-fetch-logos" id="btn-fetch-logos" onclick="App.fetchRadioLogos()"><svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;vertical-align:middle;margin-right:6px"><use href="#icon-download"/></svg>${this.t('btn.fetchLogos')}</button>`;
        html += '</div>';
        html += '<div id="fetch-logos-status" class="fetch-logos-status" style="display:none"></div>';

        // Country filter chips
        html += '<div class="radio-filters">';
        html += '<div class="filter-row">';
        html += '<button class="filter-chip active" onclick="App.filterRadioCountry(null, this)">All Countries</button>';
        data.countries.forEach(c => {
            html += `<button class="filter-chip" onclick="App.filterRadioCountry('${this.esc(c)}', this)">${this.esc(c)}</button>`;
        });
        html += '</div>';

        // Genre filter chips
        html += '<div class="filter-row" style="margin-top:8px">';
        html += '<button class="filter-chip active" onclick="App.filterRadioGenre(null, this)">All Genres</button>';
        data.genres.forEach(g => {
            html += `<button class="filter-chip" onclick="App.filterRadioGenre('${this.esc(g)}', this)">${this.esc(g)}</button>`;
        });
        html += '</div></div>';
        html += '</div>';

        // Station count
        html += `<div class="radio-count" id="radio-count">${data.stations.length} stations</div>`;

        // Station grid
        html += '<div class="radio-grid" id="radio-grid">';
        html += this.buildRadioCards(data.stations);
        html += '</div>';

        el.innerHTML = html;
    },

    buildRadioCards(stations) {
        return stations.map(s => {
            const playingClass = (this.isRadioPlaying && this.currentRadioStation && this.currentRadioStation.id === s.id) ? ' playing' : '';
            const logoUrl = s.logo ? `/radiologo/${this.esc(s.logo)}` : '';
            const logoHtml = logoUrl
                ? `<img src="${logoUrl}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><div class="radio-card-placeholder" style="display:none"><svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-radio"/></svg></div>`
                : `<div class="radio-card-placeholder"><svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-radio"/></svg></div>`;

            const favClass = s.isFavourite ? ' active' : '';
            return `<div class="radio-card${playingClass}" data-station-id="${s.id}" onclick="App.playRadioById(${s.id})">
                <div class="radio-card-logo">${logoHtml}</div>
                <div class="radio-card-info">
                    <div class="radio-card-name">${this.esc(s.name)}</div>
                    <div class="radio-card-desc">${this.esc(s.description)}</div>
                    <div class="radio-card-meta">
                        <span class="radio-card-country">${this.esc(s.country)}</span>
                        <span class="radio-card-genre">${this.esc(s.genre)}</span>
                    </div>
                </div>
                <button class="radio-card-fav${favClass}" onclick="event.stopPropagation(); App.toggleRadioFav(${s.id}, this)">&#10084;</button>
                <div class="radio-card-play"><svg style="width:18px;height:18px;stroke:#fff;fill:none;stroke-width:2"><use href="#icon-play"/></svg></div>
            </div>`;
        }).join('');
    },

    async playRadioById(id) {
        if (!this.radioStations || this.radioStations.length === 0) {
            const data = await this.api('radio/stations');
            if (data && data.stations) this.radioStations = data.stations;
        }
        const station = this.radioStations?.find(s => s.id === id);
        if (station) this.playRadioStation(station);
    },

    playRadioStation(station) {
        if (!station) return;
        // Track radio play count
        this.apiPost('radio/' + station.id + '/play').catch(() => {});
        // Stop any playing video
        document.querySelectorAll('video').forEach(v => { v.pause(); v.removeAttribute('src'); v.load(); });

        this.currentRadioStation = station;
        this.currentTrack = null;
        this.isRadioPlaying = true;
        this.playlist = [];
        this.playIndex = -1;

        this.audioPlayer.src = station.streamUrl;
        this.audioPlayer.play();
        this.isPlaying = true;

        // Show player bar in radio mode
        const bar = document.getElementById('player-bar');
        bar.classList.remove('player-hidden');
        bar.classList.add('radio-mode');

        const svgStyle = 'width:22px;height:22px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round';
        document.getElementById('btn-play').innerHTML = `<svg style="${svgStyle}"><use href="#icon-pause"/></svg>`;
        document.getElementById('player-title').textContent = station.name;
        document.getElementById('player-artist').innerHTML = `<span class="radio-live-indicator">&#9679; LIVE</span> ${this.esc(station.genre)} &middot; ${this.esc(station.country)}`;

        // Set cover to station logo
        const coverDiv = document.getElementById('player-cover');
        if (station.logo) {
            coverDiv.innerHTML = `<img src="/radiologo/${this.esc(station.logo)}" onerror="this.parentElement.innerHTML='<div style=\\'display:flex;align-items:center;justify-content:center;width:100%;height:100%\\'><svg style=\\'width:22px;height:22px;stroke:rgba(255,255,255,0.25);fill:none;stroke-width:1.5\\'><use href=&quot;#icon-radio&quot;/></svg></div>'" alt="">`;
        } else {
            coverDiv.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;width:100%;height:100%"><svg style="width:22px;height:22px;stroke:rgba(255,255,255,0.25);fill:none;stroke-width:1.5"><use href="#icon-radio"/></svg></div>';
        }

        // Hide track-specific buttons
        document.getElementById('btn-player-fav').style.display = 'none';
        document.getElementById('btn-player-add-playlist').style.display = 'none';
        document.getElementById('btn-player-lyrics').style.display = 'none';
        document.getElementById('progress-bar').style.display = 'none';
        document.getElementById('time-current').textContent = 'LIVE';
        document.getElementById('time-total').textContent = '';
        document.getElementById('btn-prev').style.display = 'none';
        document.getElementById('btn-next').style.display = 'none';
        document.getElementById('btn-shuffle').style.display = 'none';
        document.getElementById('btn-repeat').style.display = 'none';

        // Highlight playing card if on radio page
        document.querySelectorAll('.radio-card').forEach(c => c.classList.remove('playing'));
        const card = document.querySelector(`.radio-card[data-station-id="${station.id}"]`);
        if (card) card.classList.add('playing');

        // Start ICY/Icecast metadata polling
        this.startRadioMetadataPolling(station.streamUrl);
    },

    startRadioMetadataPolling(streamUrl) {
        this.stopRadioMetadataPolling();
        this._radioLastStreamTitle = null;
        this.fetchRadioMetadata(streamUrl);
        this._radioMetaInterval = setInterval(() => {
            if (this.isRadioPlaying && this.currentRadioStation?.streamUrl === streamUrl) {
                this.fetchRadioMetadata(streamUrl);
            } else {
                this.stopRadioMetadataPolling();
            }
        }, 15000);
    },

    stopRadioMetadataPolling() {
        if (this._radioMetaInterval) {
            clearInterval(this._radioMetaInterval);
            this._radioMetaInterval = null;
        }
        this._radioLastStreamTitle = null;
    },

    async fetchRadioMetadata(streamUrl) {
        try {
            const data = await this.api('radio/stream-metadata?url=' + encodeURIComponent(streamUrl));
            if (!this.isRadioPlaying) return;

            const title = data.streamTitle || data.icyName || '';
            if (!title || title === this._radioLastStreamTitle) return;
            this._radioLastStreamTitle = title;

            const artistEl = document.getElementById('player-artist');
            if (!artistEl) return;
            // Fade out, update, fade in
            artistEl.style.transition = 'opacity 0.15s';
            artistEl.style.opacity = '0';
            setTimeout(() => {
                if (this.isRadioPlaying) {
                    artistEl.innerHTML = `<span class="radio-live-indicator">&#9679; LIVE</span> ${this.esc(title)}`;
                }
                artistEl.style.opacity = '1';
            }, 150);
        } catch (e) {
            // Silently ignore — many streams don't support ICY metadata
        }
    },

    // ── Resource Metrics Charts ───────────────────────────────────────────────

    _startMetricsPolling() {
        this._stopMetricsPolling();
        this._fetchAndDrawMetrics();
        this._metricsInterval = setInterval(() => this._fetchAndDrawMetrics(), 15000);
    },

    _stopMetricsPolling() {
        if (this._metricsInterval) {
            clearInterval(this._metricsInterval);
            this._metricsInterval = null;
        }
    },

    async _fetchAndDrawMetrics() {
        const data = await this.api('metrics');
        if (!Array.isArray(data) || !data.length) return;
        const last     = data[data.length - 1];
        const accent   = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#4d8bf5';
        const procColor = '#2dd4bf';   // teal — NexusM process line
        const txColor   = '#fb923c';   // orange — network TX (upload)
        const rxColor   = '#4ade80';   // green  — network RX (download)

        // CPU: system + NexusM process (both in %)
        this._drawMetricsChart('metrics-cpu-canvas', data, [
            { valueFn: pt => pt.cpu,     color: accent,    fill: true,  badgeId: 'metrics-cpu-sys-badge',  formatFn: v => v.toFixed(1) + '%' },
            { valueFn: pt => pt.procCpu ?? 0, color: procColor, fill: false, badgeId: 'metrics-cpu-proc-badge', formatFn: v => v.toFixed(1) + '%' }
        ], { yMax: 100, unit: '%' });

        // RAM: system used GB + NexusM process MB (converted to GB for same axis)
        this._drawMetricsChart('metrics-ram-canvas', data, [
            { valueFn: pt => pt.ramUsed / 1024,       color: accent,    fill: true,  badgeId: 'metrics-ram-sys-badge',  formatFn: v => v.toFixed(2) + ' GB' },
            { valueFn: pt => (pt.procRamMb ?? 0) / 1024, color: procColor, fill: false, badgeId: 'metrics-ram-proc-badge', formatFn: v => (v * 1024).toFixed(0) + ' MB' }
        ], { yMax: last.ramTotal / 1024, unit: 'GB' });

        // Network: TX MB/s (upload) + RX MB/s (download) — autoscale
        this._drawMetricsChart('metrics-net-canvas', data, [
            { valueFn: pt => pt.netTxMbps ?? 0, color: txColor, fill: true,  badgeId: 'metrics-net-tx-badge', formatFn: v => v.toFixed(2) + ' MB/s' },
            { valueFn: pt => pt.netRxMbps ?? 0, color: rxColor, fill: true,  badgeId: 'metrics-net-rx-badge', formatFn: v => v.toFixed(2) + ' MB/s' }
        ], { unit: 'MB/s' });
    },

    // series: [{valueFn, color, fill, badgeId, formatFn}]
    // opts:   {yMax, unit}
    _drawMetricsChart(canvasId, data, series, opts = {}) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        const dpr = window.devicePixelRatio || 1;
        const W   = canvas.clientWidth  || canvas.offsetWidth  || 300;
        const H   = canvas.clientHeight || canvas.offsetHeight || 110;
        canvas.width  = W * dpr;
        canvas.height = H * dpr;
        ctx.scale(dpr, dpr);

        const textMuted = getComputedStyle(document.documentElement).getPropertyValue('--text-secondary').trim() || '#888';
        const { unit = '', yMax } = opts;

        const pad = { top: 10, right: 10, bottom: 22, left: 44 };
        const cW  = W - pad.left - pad.right;
        const cH  = H - pad.top  - pad.bottom;

        ctx.clearRect(0, 0, W, H);

        const now   = Date.now() / 1000;
        const minTs = now - 3 * 3600;
        const pts   = data.filter(p => p.ts >= minTs);

        if (!pts.length) {
            ctx.fillStyle = textMuted;
            ctx.font = '11px system-ui,sans-serif';
            ctx.textAlign = 'center';
            ctx.fillText('Collecting data…', W / 2, H / 2);
            return;
        }

        // Compute yRange from ALL series
        const allVals = series.flatMap(s => pts.map(s.valueFn));
        const dataMax = allVals.length ? Math.max(...allVals) : 0;
        const yRange  = yMax
            ? Math.max(dataMax * 1.1, yMax * 0.1, 1)
            : Math.max(dataMax * 1.25, 0.01);

        // Grid lines + Y-axis labels
        ctx.lineWidth = 1;
        for (let i = 0; i <= 4; i++) {
            const y = pad.top + cH - (i / 4) * cH;
            ctx.strokeStyle = 'rgba(255,255,255,0.05)';
            ctx.beginPath(); ctx.moveTo(pad.left, y); ctx.lineTo(pad.left + cW, y); ctx.stroke();
            const lv = (i / 4) * yRange;
            ctx.fillStyle = textMuted;
            ctx.font      = '9px system-ui,sans-serif';
            ctx.textAlign = 'right';
            const label = unit === '%' ? Math.round(lv) + '%'
                        : unit === 'GB' ? lv.toFixed(1)
                        : lv >= 1 ? lv.toFixed(1) : lv.toFixed(2);
            ctx.fillText(label, pad.left - 4, y + 3);
        }

        // X-axis time labels (every 30 min)
        ctx.fillStyle = textMuted;
        ctx.font      = '9px system-ui,sans-serif';
        ctx.textAlign = 'center';
        for (let m = 0; m <= 180; m += 30) {
            const ts = now - (180 - m) * 60;
            const x  = pad.left + ((ts - minTs) / (3 * 3600)) * cW;
            ctx.fillText(m === 180 ? 'now' : `-${180 - m}m`, x, H - 4);
        }

        const toX = ts => pad.left + Math.max(0, Math.min(1, (ts - minTs) / (3 * 3600))) * cW;
        const toY = v  => pad.top  + cH - Math.max(0, Math.min(1, v / yRange)) * cH;

        // Draw each series
        series.forEach((s, idx) => {
            const vals = pts.map(s.valueFn);

            // Gradient fill (only for flagged series)
            if (s.fill) {
                const grad = ctx.createLinearGradient(0, pad.top, 0, pad.top + cH);
                grad.addColorStop(0, s.color + (idx === 0 ? '40' : '28'));
                grad.addColorStop(1, s.color + '04');
                ctx.beginPath();
                ctx.moveTo(toX(pts[0].ts), toY(vals[0]));
                for (let i = 1; i < pts.length; i++) ctx.lineTo(toX(pts[i].ts), toY(vals[i]));
                ctx.lineTo(toX(pts[pts.length - 1].ts), pad.top + cH);
                ctx.lineTo(toX(pts[0].ts), pad.top + cH);
                ctx.closePath();
                ctx.fillStyle = grad;
                ctx.fill();
            }

            // Line stroke
            ctx.beginPath();
            ctx.moveTo(toX(pts[0].ts), toY(vals[0]));
            for (let i = 1; i < pts.length; i++) ctx.lineTo(toX(pts[i].ts), toY(vals[i]));
            ctx.strokeStyle = s.color;
            ctx.lineWidth   = 1.5;
            ctx.lineJoin    = 'round';
            ctx.stroke();

            // Dot at last point
            const lx = toX(pts[pts.length - 1].ts);
            const ly = toY(vals[vals.length - 1]);
            ctx.beginPath();
            ctx.arc(lx, ly, 3, 0, Math.PI * 2);
            ctx.fillStyle = s.color;
            ctx.fill();

            // Badge
            const badge = s.badgeId ? document.getElementById(s.badgeId) : null;
            if (badge) badge.textContent = s.formatFn(vals[vals.length - 1]);
        });
    },

    stopRadio() {
        this.stopRadioMetadataPolling();
        this.isRadioPlaying = false;
        this.currentRadioStation = null;
        document.getElementById('player-bar').classList.remove('radio-mode');

        // Restore track-mode UI
        document.getElementById('btn-player-fav').style.display = '';
        document.getElementById('btn-player-add-playlist').style.display = '';
        document.getElementById('btn-player-lyrics').style.display = '';
        document.getElementById('progress-bar').style.display = '';
        document.getElementById('btn-prev').style.display = '';
        document.getElementById('btn-next').style.display = '';
        document.getElementById('btn-shuffle').style.display = '';
        document.getElementById('btn-repeat').style.display = '';

        this.stopPlayer();
    },

    filterRadioCountry(country, btn) {
        this.radioCountryFilter = country;
        const row = btn?.closest('.filter-row');
        if (row) row.querySelectorAll('.filter-chip').forEach(c => c.classList.remove('active'));
        if (btn) btn.classList.add('active');
        this.applyRadioFilters();
    },

    filterRadioGenre(genre, btn) {
        this.radioGenreFilter = genre;
        const row = btn?.closest('.filter-row');
        if (row) row.querySelectorAll('.filter-chip').forEach(c => c.classList.remove('active'));
        if (btn) btn.classList.add('active');
        this.applyRadioFilters();
    },

    filterRadioStations() {
        this.radioSearchFilter = (document.getElementById('radio-search')?.value || '').toLowerCase();
        this.applyRadioFilters();
    },

    applyRadioFilters() {
        let filtered = this.radioStations;

        if (this.radioCountryFilter) {
            filtered = filtered.filter(s => s.country === this.radioCountryFilter);
        }
        if (this.radioGenreFilter) {
            filtered = filtered.filter(s => s.genre === this.radioGenreFilter);
        }
        if (this.radioSearchFilter) {
            const q = this.radioSearchFilter;
            filtered = filtered.filter(s =>
                s.name.toLowerCase().includes(q) ||
                s.description.toLowerCase().includes(q) ||
                s.country.toLowerCase().includes(q) ||
                s.genre.toLowerCase().includes(q)
            );
        }

        const grid = document.getElementById('radio-grid');
        const count = document.getElementById('radio-count');
        if (grid) grid.innerHTML = this.buildRadioCards(filtered);
        if (count) count.textContent = `${filtered.length} station${filtered.length !== 1 ? 's' : ''}`;
    },

    async fetchRadioLogos() {
        const btn = document.getElementById('btn-fetch-logos');
        const statusDiv = document.getElementById('fetch-logos-status');
        if (!btn || !statusDiv) return;

        btn.disabled = true;
        btn.innerHTML = '<div class="spinner" style="width:14px;height:14px;border-width:2px;display:inline-block;vertical-align:middle;margin-right:6px"></div>Fetching...';
        statusDiv.style.display = 'block';
        statusDiv.textContent = 'Starting logo fetch...';

        try {
            const res = await this.apiPost('radio/fetch-logos');
            if (!res) {
                statusDiv.innerHTML = '<span style="color:var(--danger)">Failed to start logo fetch</span>';
                btn.disabled = false;
                btn.innerHTML = '<svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;vertical-align:middle;margin-right:6px"><use href="#icon-download"/></svg>Fetch Logos';
                return;
            }
        } catch(e) {
            statusDiv.innerHTML = '<span style="color:var(--danger)">Failed to start</span>';
            btn.disabled = false;
            return;
        }

        // Poll progress
        this._logoFetchInterval = setInterval(async () => {
            try {
                const status = await this.api('radio/fetch-logos/status');
                if (!status) return;

                const pct = status.total > 0 ? Math.round((status.progress / status.total) * 100) : 0;
                statusDiv.innerHTML = `<div class="fetch-progress-bar"><div class="fetch-progress-fill" style="width:${pct}%"></div></div>`
                    + `<span class="fetch-progress-text">${status.progress}/${status.total} - ${this.esc(status.status)}</span>`;

                if (!status.isFetching) {
                    clearInterval(this._logoFetchInterval);
                    statusDiv.innerHTML = `<span style="color:var(--success)">${status.success} logos downloaded, ${status.failed} failed</span>`;
                    btn.disabled = false;
                    btn.innerHTML = '<svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;vertical-align:middle;margin-right:6px"><use href="#icon-download"/></svg>Fetch Logos';

                    // Reload stations to get updated logo paths
                    const data = await this.api('radio/stations');
                    if (data && data.stations) {
                        this.radioStations = data.stations;
                        this.applyRadioFilters();
                    }
                }
            } catch(e) { /* continue polling */ }
        }, 1000);
    },

    // ─── Internet TV ──────────────────────────────────────────
    async renderInternetTv(el) {
        const data = await this.api('tvchannels');
        const channels = data?.channels || [];
        this.tvChannels = channels;
        this.tvCountryFilter = '';
        this.tvGenreFilter = '';

        let html = `<div class="page-header"><h1>${this.t('page.internetTv')}</h1></div>`;
        html += `<div class="radio-header">
            <div class="radio-toolbar">
                <input type="text" class="radio-search-input" placeholder="${this.t('search.tvPlaceholder')}" oninput="App.filterTvChannels(this.value)">
                <label class="btn-import-m3u" title="Import M3U playlist">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-folder"/></svg>${this.t('btn.importM3u')}
                    <input type="file" accept=".m3u,.m3u8" style="display:none" onchange="App.importM3u(this)">
                </label>
                <button class="btn-fetch-logos" id="btn-fetch-tv-logos" onclick="App.fetchTvLogos()">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-refresh"/></svg>${this.t('btn.fetchLogos')}
                </button>
            </div>
            <div id="fetch-tv-logos-status" class="fetch-logos-status" style="display:none"></div>`;

        // Country filter chips
        const countries = data?.countries || [];
        if (countries.length > 0) {
            html += `<div class="radio-filters"><div class="filter-row">
                <button class="filter-chip active" onclick="App.filterTvCountry('', this)">All</button>`;
            countries.forEach(c => { html += `<button class="filter-chip" onclick="App.filterTvCountry('${this.esc(c)}', this)">${this.esc(c)}</button>`; });
            html += `</div></div>`;
        }

        // Genre filter chips
        const genres = data?.genres || [];
        if (genres.length > 0) {
            html += `<div class="radio-filters"><div class="filter-row">
                <button class="filter-chip active" onclick="App.filterTvGenre('', this)">All Genres</button>`;
            genres.forEach(g => { html += `<button class="filter-chip" onclick="App.filterTvGenre('${this.esc(g)}', this)">${this.esc(g)}</button>`; });
            html += `</div></div>`;
        }

        html += `<div class="radio-count" id="tv-count">${channels.length} channel${channels.length !== 1 ? 's' : ''}</div>`;
        html += `</div>`;
        html += `<div class="radio-grid" id="tv-grid">${this.buildTvCards(channels)}</div>`;

        el.innerHTML = html;
    },

    buildTvCards(channels) {
        if (!channels || channels.length === 0)
            return `<div class="empty-state"><div class="empty-icon">&#128250;</div><h2>${this.t('empty.noTv.title')}</h2><p>${this.t('empty.noTv.desc')}</p></div>`;

        return channels.map(c => {
            const logoSrc = c.logo ? `/tvlogo/${c.logo}` : '';
            const favClass = c.isFavourite ? 'active' : '';
            const res = c.resolution ? `<span class="tv-res-badge">${this.esc(c.resolution)}</span>` : '';
            return `<div class="radio-card" data-tv-id="${c.id}" onclick="App.playTvChannel(${c.id})">
                <div class="radio-card-logo">
                    ${logoSrc
                        ? `<img src="${logoSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="radio-card-logo-placeholder" style="display:none"><svg style="width:28px;height:28px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-tv"/></svg></span>`
                        : `<span class="radio-card-logo-placeholder"><svg style="width:28px;height:28px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-tv"/></svg></span>`}
                </div>
                <div class="radio-card-info">
                    <div class="radio-card-name">${this.esc(c.name)} ${res}</div>
                    <div class="radio-card-desc">${this.esc(c.description || c.genre || '')}</div>
                    <div class="radio-card-meta">${this.esc(c.country || '')} ${c.genre ? '&middot; ' + this.esc(c.genre) : ''}</div>
                </div>
                <button class="radio-card-fav ${favClass}" onclick="event.stopPropagation(); App.toggleTvFav(${c.id}, this)" title="Favourite">&#10084;</button>
                <div class="radio-card-play"><svg style="width:16px;height:16px;fill:white;stroke:none"><polygon points="5,3 15,10 5,17"/></svg></div>
            </div>`;
        }).join('');
    },

    playTvChannel(id) {
        const channel = this.tvChannels?.find(c => c.id === id);
        if (!channel) return;

        // Track play count
        this.apiPost('radio/' + id + '/play').catch(() => {});

        // For HLS streams (.m3u8), use a video element
        // Stop any audio playing
        this.audioPlayer.pause();
        this.audioPlayer.src = '';
        this.isPlaying = false;
        if (this.isRadioPlaying) {
            this.isRadioPlaying = false;
            this.currentRadioStation = null;
            document.getElementById('player-bar').classList.remove('radio-mode');
        }

        // Stop existing video
        document.querySelectorAll('video').forEach(v => { v.pause(); v.removeAttribute('src'); v.load(); });

        // Create fullscreen video overlay for TV
        let overlay = document.getElementById('tv-player-overlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'tv-player-overlay';
            overlay.className = 'tv-player-overlay';
            overlay.innerHTML = `
                <div class="tv-player-header">
                    <span class="tv-player-title" id="tv-player-title"></span>
                    <div class="tv-player-actions">
                        <button class="tv-player-fav" id="tv-player-fav" onclick="App.toggleTvFavFromPlayer()" title="Add to Favorites">
                            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2"><path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78L12 21.23l8.84-8.84a5.5 5.5 0 0 0 0-7.78z"/></svg>
                        </button>
                        <button class="tv-player-close" onclick="App.closeTvPlayer()">&#10005;</button>
                    </div>
                </div>
                <video id="tv-video" controls autoplay style="width:100%;height:100%;background:#000"></video>`;
            document.body.appendChild(overlay);
        }

        overlay.style.display = 'flex';
        this._currentTvChannelId = id;
        document.getElementById('tv-player-title').textContent = channel.name;
        const favBtn = document.getElementById('tv-player-fav');
        if (favBtn) favBtn.classList.toggle('active', !!channel.isFavourite);
        const video = document.getElementById('tv-video');
        this.playVideoStream(video, channel.streamUrl);

        // Highlight playing card
        document.querySelectorAll('.radio-card[data-tv-id]').forEach(c => c.classList.remove('playing'));
        const card = document.querySelector(`.radio-card[data-tv-id="${id}"]`);
        if (card) card.classList.add('playing');
    },

    closeTvPlayer() {
        const overlay = document.getElementById('tv-player-overlay');
        if (overlay) {
            overlay.querySelectorAll('video').forEach(v => this.stopVideoStream(v));
            overlay.querySelectorAll('iframe').forEach(f => { f.src = ''; });
            overlay.style.display = 'none';
        }
    },

    async toggleTvFav(id, btn) {
        const res = await this.apiPost(`tvchannels/${id}/favourite`);
        if (res && res.isFavourite !== undefined) {
            btn.classList.toggle('active', res.isFavourite);
            const ch = this.tvChannels?.find(c => c.id === id);
            if (ch) ch.isFavourite = res.isFavourite;
            // Sync player heart if this channel is currently playing
            if (this._currentTvChannelId === id) {
                const playerFav = document.getElementById('tv-player-fav');
                if (playerFav) playerFav.classList.toggle('active', res.isFavourite);
            }
        }
    },

    async toggleTvFavFromPlayer() {
        if (!this._currentTvChannelId) return;
        const id = this._currentTvChannelId;
        const res = await this.apiPost(`tvchannels/${id}/favourite`);
        if (res && res.isFavourite !== undefined) {
            const playerFav = document.getElementById('tv-player-fav');
            if (playerFav) playerFav.classList.toggle('active', res.isFavourite);
            const ch = this.tvChannels?.find(c => c.id === id);
            if (ch) ch.isFavourite = res.isFavourite;
            // Sync card heart in the grid
            const cardBtn = document.querySelector(`.radio-card[data-tv-id="${id}"] .fav-btn`);
            if (cardBtn) cardBtn.classList.toggle('active', res.isFavourite);
        }
    },

    filterTvCountry(country, btn) {
        this.tvCountryFilter = country;
        const row = btn?.closest('.filter-row');
        if (row) row.querySelectorAll('.filter-chip').forEach(c => c.classList.remove('active'));
        if (btn) btn.classList.add('active');
        this.applyTvFilters();
    },

    filterTvGenre(genre, btn) {
        this.tvGenreFilter = genre;
        const row = btn?.closest('.filter-row');
        if (row) row.querySelectorAll('.filter-chip').forEach(c => c.classList.remove('active'));
        if (btn) btn.classList.add('active');
        this.applyTvFilters();
    },

    filterTvChannels(query) {
        this.tvSearchFilter = query;
        this.applyTvFilters();
    },

    applyTvFilters() {
        let filtered = this.tvChannels || [];
        if (this.tvCountryFilter) filtered = filtered.filter(c => c.country === this.tvCountryFilter);
        if (this.tvGenreFilter) filtered = filtered.filter(c => c.genre === this.tvGenreFilter);
        if (this.tvSearchFilter) {
            const q = this.tvSearchFilter.toLowerCase();
            filtered = filtered.filter(c => c.name.toLowerCase().includes(q) || (c.description||'').toLowerCase().includes(q) || (c.country||'').toLowerCase().includes(q));
        }
        const grid = document.getElementById('tv-grid');
        const count = document.getElementById('tv-count');
        if (grid) grid.innerHTML = this.buildTvCards(filtered);
        if (count) count.textContent = `${filtered.length} channel${filtered.length !== 1 ? 's' : ''}`;
    },

    async importM3u(input) {
        const file = input.files[0];
        if (!file) return;

        const formData = new FormData();
        formData.append('file', file);

        // Show loading
        const grid = document.getElementById('tv-grid');
        if (grid) grid.innerHTML = '<div class="spinner"></div>';

        try {
            const res = await fetch('/api/tvchannels/import', {
                method: 'POST',
                body: formData
            });
            const data = await res.json();
            if (res.ok) {
                alert(`${data.message}`);
                this.renderInternetTv(document.getElementById('main-content'));
            } else {
                alert(data.message || 'Import failed');
            }
        } catch(e) {
            alert('Import failed: ' + e.message);
        }
        input.value = '';
    },

    async fetchTvLogos() {
        const btn = document.getElementById('btn-fetch-tv-logos');
        const statusDiv = document.getElementById('fetch-tv-logos-status');
        if (!btn || !statusDiv) return;

        btn.disabled = true;
        btn.innerHTML = '<div class="spinner" style="width:14px;height:14px;border-width:2px;margin-right:8px"></div>Fetching...';
        statusDiv.style.display = 'block';
        statusDiv.textContent = 'Starting logo fetch...';

        try {
            const res = await this.apiPost('tvchannels/fetch-logos');
            if (!res || res.message?.includes('already')) {
                statusDiv.textContent = 'Logo fetch already in progress...';
            }
        } catch(e) {
            statusDiv.textContent = 'Failed to start logo fetch.';
            btn.disabled = false;
            btn.innerHTML = '<svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-refresh"/></svg>Fetch Logos';
            return;
        }

        // Poll progress
        this._tvLogoFetchInterval = setInterval(async () => {
            try {
                const status = await this.api('tvchannels/fetch-logos/status');
                if (!status) return;

                const pct = status.total > 0 ? Math.round(status.progress / status.total * 100) : 0;
                statusDiv.innerHTML = `<div class="fetch-progress-bar"><div class="fetch-progress-fill" style="width:${pct}%"></div></div>
                    <div class="fetch-progress-text">${status.status} (${status.progress}/${status.total}) — ${status.success} found, ${status.failed} failed</div>`;

                if (!status.isFetching) {
                    clearInterval(this._tvLogoFetchInterval);
                    btn.disabled = false;
                    btn.innerHTML = '<svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-refresh"/></svg>Fetch Logos';
                    // Reload page to show logos
                    setTimeout(() => this.renderInternetTv(document.getElementById('main-content')), 1500);
                }
            } catch(e) { /* continue polling */ }
        }, 1000);
    },

    // ─── Podcasts ──────────────────────────────────────────────

    async renderPodcasts(el) {
        this.stopPlayer();
        this.closeTvPlayer();
        this.podcastCurrentFeed = null;
        this.podcastSearchFilter = '';
        const feeds = await this.api('podcasts');
        if (feeds === null) { el.innerHTML = this.emptyState('Error', this.t('podcasts.loadError')); return; }
        this.podcastFeeds = feeds;

        let html = `<div class="radio-header">
            <div class="radio-toolbar">
                <input type="text" class="radio-search-input" id="podcast-search" placeholder="${this.t('search.podcastsPlaceholder')}" oninput="App.filterPodcasts()" value="">
                <button class="btn-import-m3u" onclick="App.addPodcastFeed()" title="${this.t('btn.addFeed')}">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-plus"/></svg>${this.t('btn.addFeed')}
                </button>
                <label class="btn-import-m3u" style="cursor:pointer" title="${this.t('btn.importOpml')}">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-upload"/></svg>${this.t('btn.importOpml')}
                    <input type="file" accept=".opml,.xml" style="display:none" onchange="App.importOpml(this)">
                </label>
                <button class="btn-fetch-logos" id="btn-podcast-refresh" onclick="App.refreshPodcasts(this)" title="${this.t('btn.refresh')}">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-refresh"/></svg>${this.t('btn.refresh')}
                </button>
            </div>
        </div>`;

        if (feeds.length === 0) {
            html += `<div style="color:var(--text-muted);font-size:0.9rem;margin-bottom:20px">${this.t('podcasts.noSubscriptions')}</div>`;
        } else {
            html += `<div class="home-section-header" style="margin-bottom:12px">
                <h2 class="home-section-title">
                    <span class="home-section-icon"><svg style="width:18px;height:18px;stroke:var(--accent);fill:none;stroke-width:2"><use href="#icon-heart"/></svg></span>
                    ${this.t('podcasts.mySubscriptions', 'My Subscriptions')}
                    <span class="radio-count" id="podcast-count" style="margin-left:8px;font-size:0.78rem;font-weight:400;color:var(--text-muted)">${feeds.length} ${this.t(feeds.length !== 1 ? 'misc.podcasts' : 'misc.podcast')}</span>
                </h2>
            </div>
            <div class="radio-grid" style="margin-bottom:32px" id="podcast-grid">${this.buildPodcastCards(feeds)}</div>`;
        }

        html += this.buildPodcastDiscoverSection(feeds);
        el.innerHTML = html;
    },

    buildPodcastCards(feeds) {
        if (!feeds.length) return '';
        return feeds.map(f => {
            const art = f.artworkFile ? `/podcastart/${f.artworkFile}` : '';
            const logo = art
                ? `<img src="${art}" alt="" class="radio-card-logo" style="border-radius:8px" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">`
                : '';
            const placeholder = `<div class="radio-card-placeholder" style="${art ? 'display:none' : ''}">
                <svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-podcast"/></svg></div>`;
            const unplayed = f.unplayedCount > 0
                ? `<span class="podcast-unplayed-badge">${f.unplayedCount}</span>` : '';
            const fav = `<button class="radio-card-fav${f.isFavourite ? ' active' : ''}" onclick="App.togglePodcastFav(${f.id},this)" title="${this.t('btn.favourite', 'Favorite')}">♥</button>`;
            return `<div class="radio-card" id="podcast-card-${f.id}" ondblclick="App.openPodcast(${f.id})">
                <div class="radio-card-logo" style="position:relative;cursor:pointer" onclick="App.openPodcast(${f.id})">${logo}${placeholder}</div>
                <div class="radio-card-info" style="cursor:pointer" onclick="App.openPodcast(${f.id})">
                    <div class="radio-card-name">${this.esc(f.title)} ${unplayed}</div>
                    <div class="radio-card-desc">${this.esc(f.author || f.category || '')}</div>
                    <div class="radio-card-meta">
                        <span class="radio-card-country">${this.esc(f.category || '')}</span>
                        ${f.episodeCount ? `<span class="radio-card-genre">${f.episodeCount} ${this.t('podcasts.episodes')}</span>` : ''}
                    </div>
                </div>
                ${fav}
                <button class="radio-card-play" onclick="App.openPodcast(${f.id})" title="${this.t('btn.open')}">
                    <svg style="width:16px;height:16px;fill:currentColor"><use href="#icon-play"/></svg>
                </button>
                <button class="podcast-delete-btn" onclick="App.deletePodcast(${f.id})" title="${this.t('btn.unsubscribe')}">✕</button>
            </div>`;
        }).join('');
    },

    filterPodcasts() {
        this.podcastSearchFilter = (document.getElementById('podcast-search')?.value || '').toLowerCase();
        const filtered = this.podcastFeeds.filter(f =>
            !this.podcastSearchFilter ||
            f.title.toLowerCase().includes(this.podcastSearchFilter) ||
            (f.author || '').toLowerCase().includes(this.podcastSearchFilter) ||
            (f.category || '').toLowerCase().includes(this.podcastSearchFilter)
        );
        const grid = document.getElementById('podcast-grid');
        if (grid) grid.innerHTML = this.buildPodcastCards(filtered);
        const count = document.getElementById('podcast-count');
        if (count) count.textContent = `${filtered.length} ${this.t(filtered.length !== 1 ? 'misc.podcasts' : 'misc.podcast')}`;
    },

    async openPodcast(feedId) {
        this.stopPlayer();
        this.closeTvPlayer();
        const feed = this.podcastFeeds.find(f => f.id === feedId);
        if (!feed) return;
        this.podcastCurrentFeed = feed;
        const episodes = await this.api(`podcasts/${feedId}/episodes`);
        if (!episodes) return;
        this.podcastEpisodes = episodes;
        const el = document.getElementById('main-content');
        const art = feed.artworkFile ? `/podcastart/${feed.artworkFile}` : '';
        let html = `<div id="podcast-now-playing" class="podcast-np-hidden"></div>
        <div class="podcast-back-btn" onclick="App.renderPodcasts(document.getElementById('main-content'))">
            ${this.t('podcasts.backToShows')}
        </div>
        <div style="display:flex;align-items:center;gap:16px;margin-bottom:20px">
            ${art ? `<img src="${art}" alt="" style="width:80px;height:80px;border-radius:10px;object-fit:cover">` : ''}
            <div>
                <div style="font-size:1.2rem;font-weight:700;color:var(--text-primary)">${this.esc(feed.title)}</div>
                <div style="color:var(--text-secondary);font-size:0.85rem">${this.esc(feed.author || '')}</div>
                <div style="color:var(--text-muted);font-size:0.8rem">${episodes.length} ${this.t(episodes.length !== 1 ? 'podcasts.episodes' : 'podcasts.episode')}</div>
            </div>
        </div>
        <div class="radio-toolbar" style="margin-bottom:12px">
            <input type="text" class="radio-search-input" id="episode-search" placeholder="${this.t('search.episodesPlaceholder')}" oninput="App.filterEpisodes()">
        </div>
        <div class="podcast-episode-list" id="episode-list">${this.buildEpisodeList(episodes)}</div>`;
        el.innerHTML = html;
        // If a podcast episode is currently playing for this feed, restore the panel
        if (this.isPlaying && this._currentPodcastEp && this._currentPodcastEp.feedId === feedId) {
            this.renderPodcastNowPlaying(this._currentPodcastEp);
        }
    },

    buildEpisodeList(episodes) {
        if (!episodes.length) return `<div style="color:var(--text-muted);padding:24px 0">${this.t('podcasts.noEpisodes')}</div>`;
        return episodes.map(ep => {
            const dur = ep.durationSeconds > 0 ? this.formatDuration(ep.durationSeconds) : '';
            const date = ep.publishDate ? new Date(ep.publishDate).toLocaleDateString() : '';
            const typeBadge = `<span class="podcast-type-badge ${ep.mediaType === 'video' ? 'podcast-type-video' : ''}">${ep.mediaType.toUpperCase()}</span>`;
            const playedClass = ep.isPlayed ? ' podcast-episode-played' : '';
            const resume = ep.playPositionSeconds > 0 && !ep.isPlayed
                ? `<span style="font-size:0.75rem;color:var(--accent);margin-left:6px">▶ ${this.formatDuration(ep.playPositionSeconds)}</span>` : '';
            return `<div class="podcast-episode-row${playedClass}" id="ep-row-${ep.id}" onclick="App.playPodcastEpisode(${ep.id})">
                <button class="podcast-played-btn" onclick="event.stopPropagation();App.toggleEpisodePlayed(${ep.feedId},${ep.id},this)" title="${ep.isPlayed ? this.t('podcasts.markUnplayed') : this.t('podcasts.markPlayed')}">
                    ${ep.isPlayed ? '✓' : '○'}
                </button>
                <div class="podcast-episode-info">
                    <div class="podcast-episode-title">${this.esc(ep.title)}</div>
                    <div class="podcast-episode-meta">${date}${dur ? ` · ${dur}` : ''}${resume}</div>
                </div>
                ${typeBadge}
                <button class="radio-card-play" style="flex-shrink:0" onclick="event.stopPropagation();App.playPodcastEpisode(${ep.id})" title="${this.t('btn.play')}">
                    <svg style="width:16px;height:16px;fill:currentColor"><use href="#icon-play"/></svg>
                </button>
            </div>`;
        }).join('');
    },

    filterEpisodes() {
        const q = (document.getElementById('episode-search')?.value || '').toLowerCase();
        const filtered = this.podcastEpisodes.filter(e =>
            !q || e.title.toLowerCase().includes(q) || (e.description || '').toLowerCase().includes(q)
        );
        const list = document.getElementById('episode-list');
        if (list) list.innerHTML = this.buildEpisodeList(filtered);
    },

    async addPodcastFeed() {
        const url = prompt(this.t('podcasts.enterFeedUrl'));
        if (!url) return;
        const res = await this.apiPost('podcasts', { url });
        if (res && res.feedId != null) {
            await this.renderPodcasts(document.getElementById('main-content'));
        } else if (res && res.message) {
            alert(res.message);
        }
    },

    async importOpml(input) {
        const file = input.files[0];
        if (!file) return;
        const formData = new FormData();
        formData.append('file', file);
        input.value = '';
        try {
            const res = await fetch('/api/podcasts/import-opml', {
                method: 'POST', body: formData, credentials: 'include'
            });
            const data = await res.json();
            alert(this.t('podcasts.importResult').replace('{0}', data.imported).replace('{1}', data.skipped).replace('{2}', data.failed));
            await this.renderPodcasts(document.getElementById('main-content'));
        } catch(e) {
            alert(this.t('podcasts.importFailed'));
        }
    },

    async refreshPodcasts(btn) {
        if (btn) { btn.disabled = true; btn.innerHTML = `<svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-refresh"/></svg>${this.t('podcasts.refreshing')}`; }
        await this.apiPost('podcasts/refresh');
        setTimeout(async () => {
            await this.renderPodcasts(document.getElementById('main-content'));
        }, 3000);
    },

    playPodcastEpisode(ep) {
        if (typeof ep === 'number') {
            ep = this.podcastEpisodes.find(e => e.id === ep);
            if (!ep) return;
        }
        if (typeof ep === 'string') { try { ep = JSON.parse(ep); } catch(e) { return; } }
        if (ep.mediaType === 'video') {
            // Use TV-style fullscreen overlay
            this.playVideoOverlay(ep.mediaUrl, ep.title);
        } else {
            // Use the audio player bar
            if (this.isRadioPlaying) this.stopRadio();
            this.stopPlayer();
            this._currentPodcastEp = ep;
            this.audioPlayer.src = `/api/podcasts/proxy?url=${encodeURIComponent(ep.mediaUrl)}`;
            this.connectEQToElement(this.audioPlayer);
            if (ep.playPositionSeconds > 0) this.audioPlayer.currentTime = ep.playPositionSeconds;
            this.audioPlayer.play().catch(() => {});
            this.isPlaying = true;
            document.getElementById('btn-eq').style.display = '';
            const svgStyle = 'width:22px;height:22px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round';
            document.getElementById('btn-play').innerHTML = `<svg style="${svgStyle}"><use href="#icon-pause"/></svg>`;
            const bar = document.querySelector('.player-bar');
            if (bar) { bar.classList.remove('player-hidden'); bar.classList.add('podcast-mode'); }
            document.getElementById('player-title').textContent = ep.title;
            document.getElementById('player-artist').textContent = this.podcastCurrentFeed ? this.podcastCurrentFeed.title : 'Podcast';
            // Show podcast artwork in bottom bar cover
            const cover = document.getElementById('player-cover');
            const art = this.podcastCurrentFeed?.artworkFile ? `/podcastart/${this.podcastCurrentFeed.artworkFile}` : '';
            if (cover) cover.innerHTML = art
                ? `<img src="${art}" style="width:100%;height:100%;object-fit:cover;border-radius:4px">`
                : `<div class="player-placeholder" style="display:flex;align-items:center;justify-content:center;width:100%;height:100%"><svg><use href="#icon-music-note"/></svg></div>`;
            // Render the top now-playing panel (if on the podcast detail page)
            this.renderPodcastNowPlaying(ep);
            // Save position every 10s
            this._podcastPositionInterval = setInterval(async () => {
                if (!this.audioPlayer.paused && ep.id) {
                    await this.apiPost(`podcasts/${ep.feedId}/ep/${ep.id}/progress`, { position: Math.floor(this.audioPlayer.currentTime) });
                }
            }, 10000);
        }
    },

    renderPodcastNowPlaying(ep) {
        const panel = document.getElementById('podcast-now-playing');
        if (!panel) return;
        const feed = this.podcastCurrentFeed;
        const art = feed?.artworkFile ? `/podcastart/${feed.artworkFile}` : '';
        const artHtml = art
            ? `<img src="${art}" class="podcast-np-art" alt="">`
            : `<div class="podcast-np-art-placeholder"><svg style="width:36px;height:36px;fill:none;stroke:var(--text-muted);stroke-width:1.5"><use href="#icon-podcast"/></svg></div>`;
        const svgStyle = 'width:24px;height:24px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round';
        panel.className = 'podcast-now-playing';
        panel.innerHTML = `
            ${artHtml}
            <div class="podcast-np-body">
                <div class="podcast-np-title" title="${this.esc(ep.title)}">${this.esc(ep.title)}</div>
                <div class="podcast-np-show">${this.esc(feed?.title || 'Podcast')}</div>
                <div class="podcast-np-progress">
                    <span class="podcast-np-time" id="pnp-cur">0:00</span>
                    <div class="podcast-np-bar" id="pnp-bar">
                        <div class="podcast-np-fill" id="pnp-fill" style="width:0%"></div>
                        <div class="podcast-np-thumb" id="pnp-thumb" style="left:0%"></div>
                    </div>
                    <span class="podcast-np-time" id="pnp-tot">--:--</span>
                </div>
                <div class="podcast-np-controls">
                    <button class="podcast-np-btn podcast-np-skip" onclick="App.podcastSkip(-30)" title="Back 30 seconds">
                        <svg style="${svgStyle}"><use href="#icon-skip-back"/></svg>-30s
                    </button>
                    <button class="podcast-np-btn podcast-np-play" id="pnp-play-btn" onclick="App.togglePlay()">
                        <svg style="${svgStyle}"><use href="#icon-${this.isPlaying ? 'pause' : 'play'}"/></svg>
                    </button>
                    <button class="podcast-np-btn podcast-np-skip" onclick="App.podcastSkip(30)" title="Forward 30 seconds">
                        <svg style="${svgStyle}"><use href="#icon-skip-forward"/></svg>+30s
                    </button>
                </div>
            </div>`;
        const pnpBar = document.getElementById('pnp-bar');
        if (pnpBar) {
            pnpBar.addEventListener('click', e => {
                const pct = (e.clientX - pnpBar.getBoundingClientRect().left) / pnpBar.offsetWidth;
                if (this.audioPlayer.duration) this.audioPlayer.currentTime = Math.max(0, pct * this.audioPlayer.duration);
            });
        }
    },

    podcastSkip(seconds) {
        if (!this.audioPlayer.src) return;
        this.audioPlayer.currentTime = Math.max(0, Math.min(
            this.audioPlayer.currentTime + seconds,
            this.audioPlayer.duration || Infinity
        ));
    },

    playVideoOverlay(url, title) {
        let overlay = document.getElementById('tv-player-overlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'tv-player-overlay';
            overlay.className = 'tv-player-overlay';
            document.body.appendChild(overlay);
        }
        const isYouTube = url.includes('youtube.com/embed/');
        const player = isYouTube
            ? `<iframe src="${url}?autoplay=1" style="width:100%;height:calc(100% - 44px);border:none;background:#000" allow="autoplay; fullscreen" allowfullscreen></iframe>`
            : `<video id="podcast-video-el" controls autoplay style="width:100%;height:calc(100% - 44px);background:#000"></video>`;
        overlay.innerHTML = `
            <div class="tv-player-header">
                <span class="tv-player-title">${this.esc(title || '')}</span>
                <button class="tv-close-btn" onclick="App.closeTvPlayer()">✕</button>
            </div>
            ${player}`;
        overlay.style.display = 'flex';
        if (!isYouTube) {
            const video = document.getElementById('podcast-video-el');
            if (video) this.playVideoStream(video, url);
        }
    },

    async toggleEpisodePlayed(feedId, epId, btn) {
        const res = await this.apiPost(`podcasts/${feedId}/ep/${epId}/played`);
        if (!res) return;
        const row = document.getElementById(`ep-row-${epId}`);
        if (row) {
            if (res.isPlayed) { row.classList.add('podcast-episode-played'); }
            else { row.classList.remove('podcast-episode-played'); }
        }
        if (btn) btn.textContent = res.isPlayed ? '✓' : '○';
        const ep = this.podcastEpisodes.find(e => e.id === epId);
        if (ep) ep.isPlayed = res.isPlayed;
    },

    async togglePodcastFav(feedId, btn) {
        const res = await this.apiPost(`podcasts/${feedId}/favourite`);
        if (!res) return;
        if (btn) btn.classList.toggle('active', res.isFavourite);
        const feed = this.podcastFeeds.find(f => f.id === feedId);
        if (feed) feed.isFavourite = res.isFavourite;
        // If called from the Favourites page, remove the card when unfavourited
        if (!res.isFavourite && this.currentPage === 'favourites') {
            btn?.closest('.radio-card')?.remove();
        }
    },

    async deletePodcast(feedId) {
        if (!confirm('Unsubscribe from this podcast? All episode data will be removed.')) return;
        await this.apiDelete(`podcasts/${feedId}`);
        this.podcastFeeds = this.podcastFeeds.filter(f => f.id !== feedId);
        const card = document.getElementById(`podcast-card-${feedId}`);
        if (card) card.remove();
    },

    buildPodcastDiscoverSection(subscribedFeeds) {
        const subscribedUrls = new Set((subscribedFeeds || []).map(f => f.rssUrl));

        const audioSuggestions = [
            { title: 'BBC Global News Podcast', author: 'BBC', category: 'News', url: 'https://podcasts.files.bbci.co.uk/p02nq0gn.rss' },
            { title: 'NPR News Now', author: 'NPR', category: 'News', url: 'https://feeds.npr.org/500005/podcast.xml' },
            { title: 'TED Talks Daily', author: 'TED', category: 'Education', url: 'https://feeds.feedburner.com/TEDTalks_audio' },
            { title: 'The Daily', author: 'The New York Times', category: 'News', url: 'https://feeds.simplecast.com/54nAGcIl' },
            { title: 'Serial', author: 'Serial Productions', category: 'True Crime', url: 'https://feeds.serialpodcast.org/serialpodcast' },
            { title: 'Stuff You Should Know', author: 'iHeart Podcasts', category: 'Education', url: 'https://feeds.megaphone.fm/stuffyoushouldknow' },
            { title: 'How I Built This', author: 'NPR / Guy Raz', category: 'Business', url: 'https://feeds.npr.org/510313/podcast.xml' },
            { title: 'Radiolab', author: 'WNYC Studios', category: 'Science', url: 'https://feeds.feedburner.com/radiolab' },
            { title: 'Science Vs', author: 'Spotify Studios', category: 'Science', url: 'https://feeds.megaphone.fm/sciencevs' },
            { title: 'Darknet Diaries', author: 'Jack Rhysider', category: 'Technology', url: 'https://feeds.megaphone.fm/darknetdiaries' },
            { title: 'Crime Junkie', author: 'audiochuck', category: 'True Crime', url: 'https://feeds.simplecast.com/qm_9xx0g' },
            { title: 'Conan O\'Brien Needs a Friend', author: 'Team Coco', category: 'Comedy', url: 'https://feeds.simplecast.com/dHoohVNH' },
            { title: 'Huberman Lab', author: 'Andrew Huberman', category: 'Health', url: 'https://feeds.megaphone.fm/hubermanlab' },
            { title: 'No Such Thing as a Fish', author: 'QI', category: 'Education', url: 'https://feeds.feedburner.com/NoSuchThingAsAFish' },
            { title: 'The Joe Rogan Experience', author: 'Joe Rogan', category: 'Comedy', url: 'https://feeds.megaphone.fm/GLT1412515089' }
        ].filter(s => !subscribedUrls.has(s.url));

        const videoSuggestions = [
            { title: 'TED Talks (HD Video)', author: 'TED', category: 'Education', url: 'https://feeds.feedburner.com/TEDTalks_video' },
            { title: 'H3 Podcast', author: 'Ethan & Hila Klein', category: 'Comedy', url: 'https://www.youtube.com/feeds/videos.xml?channel_id=UCLtREJY21xRfCuEKvdki1Kw' },
            { title: 'The Joe Rogan Experience (Video)', author: 'Joe Rogan', category: 'Comedy', url: 'https://www.youtube.com/feeds/videos.xml?channel_id=UCZOEywSEwg8YZcKMoI5mHgA' }
        ].filter(s => !subscribedUrls.has(s.url));

        const buildCards = (list, icon) => list.map(s => {
            const safeUrl = s.url.replace(/'/g,"\\'");
            const safeTitle = s.title.replace(/'/g,"\\'");
            const safeAuthor = (s.author||'').replace(/'/g,"\\'");
            const safeCat = (s.category||'').replace(/'/g,"\\'");
            return `
            <div class="radio-card podcast-discover-card" onclick="App.openDiscoverPreview('${safeUrl}','${safeTitle}','${safeAuthor}','${safeCat}')" style="cursor:pointer">
                <div class="radio-card-logo">
                    <div class="radio-card-placeholder">
                        <svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="${icon}"/></svg>
                    </div>
                </div>
                <div class="radio-card-info">
                    <div class="radio-card-name">${this.esc(s.title)}</div>
                    <div class="radio-card-desc">${this.esc(s.author)}</div>
                    <div class="radio-card-meta"><span class="radio-card-country">${this.esc(s.category)}</span></div>
                </div>
                <button class="podcast-subscribe-btn" onclick="event.stopPropagation();App.subscribeDiscover('${safeUrl}',this)" title="${this.t('btn.subscribe')}">
                    <svg style="width:13px;height:13px;stroke:currentColor;fill:none;stroke-width:2.5"><use href="#icon-plus"/></svg>
                    ${this.t('btn.subscribe','Subscribe')}
                </button>
            </div>`;
        }).join('');

        if (!audioSuggestions.length && !videoSuggestions.length) return '';

        let html = '<div class="podcast-discover-section">';

        if (audioSuggestions.length) {
            html += `
            <div class="home-section-header" style="margin-bottom:12px">
                <h2 class="home-section-title">
                    <span class="home-section-icon"><svg style="width:18px;height:18px;stroke:var(--accent);fill:none;stroke-width:2"><use href="#icon-podcast"/></svg></span>
                    ${this.t('podcasts.discoverAudio')}
                </h2>
            </div>
            <div class="radio-grid" style="margin-bottom:24px">${buildCards(audioSuggestions, '#icon-podcast')}</div>`;
        }

        if (videoSuggestions.length) {
            html += `
            <div class="home-section-header" style="margin-bottom:12px">
                <h2 class="home-section-title">
                    <span class="home-section-icon"><svg style="width:18px;height:18px;stroke:var(--accent);fill:none;stroke-width:2"><use href="#icon-tv"/></svg></span>
                    ${this.t('podcasts.discoverVideo')}
                </h2>
            </div>
            <div class="radio-grid">${buildCards(videoSuggestions, '#icon-tv')}</div>`;
        }

        html += '</div>';
        return html;
    },

    async openDiscoverPreview(url, title, author, category) {
        const el = document.getElementById('main-content');
        // Show loading state
        el.innerHTML = `
            <div class="podcast-back-btn" onclick="App.renderPodcasts(document.getElementById('main-content'))">
                ← ${this.t('podcasts.backToDiscover', 'Back to Discover')}
            </div>
            <div class="podcast-preview-header">
                <div class="podcast-preview-art-wrap">
                    <div class="podcast-np-art-placeholder" style="width:120px;height:120px;border-radius:12px">
                        <svg style="width:40px;height:40px;fill:none;stroke:var(--text-muted);stroke-width:1.5"><use href="#icon-podcast"/></svg>
                    </div>
                </div>
                <div class="podcast-preview-meta">
                    <div class="podcast-preview-title">${this.esc(title)}</div>
                    <div class="podcast-preview-author">${this.esc(author)}</div>
                    <div class="podcast-preview-category">${this.esc(category)}</div>
                    <button class="podcast-subscribe-btn podcast-subscribe-lg" id="preview-sub-btn" onclick="App.subscribeFromPreview('${url.replace(/'/g,"\\'")}')">
                        <svg style="width:15px;height:15px;stroke:currentColor;fill:none;stroke-width:2.5"><use href="#icon-plus"/></svg>
                        ${this.t('btn.subscribe','Subscribe')}
                    </button>
                </div>
            </div>
            <div style="color:var(--text-muted);font-size:0.85rem;padding:20px 0">${this.t('podcasts.loadingPreview','Loading episodes…')}</div>`;

        // Fetch preview from backend
        const data = await this.api(`podcasts/preview?url=${encodeURIComponent(url)}`);
        if (!data) {
            document.querySelector('#main-content div:last-child').innerHTML =
                `<div style="color:var(--danger)">${this.t('podcasts.previewError','Could not load feed.')}</div>`;
            return;
        }

        // Update artwork if available
        if (data.artworkUrl) {
            document.querySelector('.podcast-preview-art-wrap').innerHTML =
                `<img src="${this.esc(data.artworkUrl)}" class="podcast-preview-art" onerror="this.style.display='none'">`;
        }
        if (data.description) {
            const meta = document.querySelector('.podcast-preview-meta');
            const desc = document.createElement('div');
            desc.className = 'podcast-preview-desc';
            desc.textContent = data.description.slice(0, 300) + (data.description.length > 300 ? '…' : '');
            meta.insertBefore(desc, document.getElementById('preview-sub-btn'));
        }

        // Build episode list
        const previewTitle = data.title || title;
        const episodes = data.episodes || [];
        const epHtml = episodes.length
            ? episodes.map((ep, i) => {
                const dur = ep.duration > 0 ? this.formatDuration(ep.duration) : '';
                const date = ep.publishDate ? new Date(ep.publishDate).toLocaleDateString() : '';
                const typeBadge = `<span class="podcast-type-badge ${ep.mediaType === 'video' ? 'podcast-type-video' : ''}">${(ep.mediaType||'audio').toUpperCase()}</span>`;
                const safeUrl = (ep.mediaUrl||'').replace(/'/g,"\\'").replace(/"/g,'&quot;');
                const safeEpTitle = this.esc(ep.title);
                const safeShowTitle = this.esc(previewTitle);
                const playBtn = ep.mediaUrl
                    ? `<button class="radio-card-play" style="flex-shrink:0" onclick="event.stopPropagation();App.playPreviewEpisode('${safeUrl}','${safeEpTitle}','${safeShowTitle}','${ep.mediaType||'audio'}',this,${i})" title="${this.t('btn.play')}">
                        <svg style="width:16px;height:16px;fill:currentColor"><use href="#icon-play"/></svg>
                       </button>`
                    : '';
                return `<div class="podcast-episode-row" id="prev-ep-${i}" onclick="App.playPreviewEpisode('${safeUrl}','${safeEpTitle}','${safeShowTitle}','${ep.mediaType||'audio'}',null,${i})">
                    <div class="podcast-episode-info">
                        <div class="podcast-episode-title">${this.esc(ep.title)}</div>
                        <div class="podcast-episode-meta">${date}${dur ? ` · ${dur}` : ''}</div>
                    </div>
                    ${typeBadge}
                    ${playBtn}
                </div>`;
            }).join('')
            : `<div style="color:var(--text-muted);padding:16px 0">${this.t('podcasts.noEpisodes')}</div>`;

        document.querySelector('#main-content div:last-child').outerHTML = `
            <div class="home-section-header" style="margin:24px 0 12px">
                <h2 class="home-section-title">${this.t('podcasts.recentEpisodes','Recent Episodes')}</h2>
            </div>
            <div class="podcast-episode-list">${epHtml}</div>`;

        this._discoverPreviewUrl = url;
    },

    async subscribeFromPreview(url) {
        const btn = document.getElementById('preview-sub-btn');
        if (btn) { btn.disabled = true; btn.innerHTML = `<svg style="width:15px;height:15px;stroke:currentColor;fill:none;stroke-width:2.5"><use href="#icon-refresh"/></svg> ${this.t('podcasts.subscribing','Subscribing…')}`; }
        const res = await this.apiPost('podcasts', { url });
        if (res && res.feedId != null) {
            await this.renderPodcasts(document.getElementById('main-content'));
        } else {
            if (res && res.message) alert(res.message);
            if (btn) { btn.disabled = false; btn.innerHTML = `<svg style="width:15px;height:15px;stroke:currentColor;fill:none;stroke-width:2.5"><use href="#icon-plus"/></svg> ${this.t('btn.subscribe','Subscribe')}`; }
        }
    },

    async subscribeDiscover(url, btn) {
        if (btn) {
            btn.disabled = true;
            btn.innerHTML = `<svg style="width:13px;height:13px;stroke:currentColor;fill:none;stroke-width:2.5"><use href="#icon-refresh"/></svg> ${this.t('podcasts.subscribing','Subscribing…')}`;
        }
        const res = await this.apiPost('podcasts', { url });
        if (res && res.feedId != null) {
            await this.renderPodcasts(document.getElementById('main-content'));
        } else {
            if (res && res.message) alert(res.message);
            if (btn) {
                btn.disabled = false;
                btn.innerHTML = `<svg style="width:13px;height:13px;stroke:currentColor;fill:none;stroke-width:2.5"><use href="#icon-plus"/></svg> ${this.t('btn.subscribe','Subscribe')}`;
            }
        }
    },

    playPreviewEpisode(mediaUrl, epTitle, showTitle, mediaType, btn, rowIndex) {
        if (!mediaUrl) return;
        if (mediaType === 'video') {
            this.playVideoOverlay(mediaUrl, epTitle);
            return;
        }
        if (this.isRadioPlaying) this.stopRadio();
        this.stopPlayer();
        this._currentPodcastEp = null;
        this.audioPlayer.src = `/api/podcasts/proxy?url=${encodeURIComponent(mediaUrl)}`;
        this.audioPlayer.play().catch(() => {});
        this.isPlaying = true;
        const svgStyle = 'width:22px;height:22px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round';
        document.getElementById('btn-play').innerHTML = `<svg style="${svgStyle}"><use href="#icon-pause"/></svg>`;
        const bar = document.querySelector('.player-bar');
        if (bar) { bar.classList.remove('player-hidden'); bar.classList.add('podcast-mode'); }
        document.getElementById('player-title').textContent = epTitle;
        document.getElementById('player-artist').textContent = showTitle;
        // Highlight playing row, reset others
        document.querySelectorAll('.podcast-episode-row').forEach((r, i) => {
            r.classList.toggle('podcast-episode-playing', i === rowIndex);
            const pb = r.querySelector('.radio-card-play svg use');
            if (pb) pb.setAttribute('href', i === rowIndex ? '#icon-pause' : '#icon-play');
        });
    },

    formatDuration(secs) {
        if (!secs) return '';
        const h = Math.floor(secs / 3600);
        const m = Math.floor((secs % 3600) / 60);
        const s = secs % 60;
        if (h > 0) return `${h}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;
        return `${m}:${String(s).padStart(2,'0')}`;
    },

    // Returns a short audio format name ("MP3", "FLAC", "AAC", "ALAC", etc.) from mimeType/codec
    trackFormat(t) {
        // Check codec first for ALAC — it shares audio/mp4 MIME type with AAC
        const c = (t.codec || '').toLowerCase();
        if (c.includes('alac')) return 'ALAC';
        const mt = (t.mimeType || '').toLowerCase();
        if (mt.includes('flac')) return 'FLAC';
        if (mt === 'audio/mpeg' || mt.includes('mpeg')) return 'MP3';
        if (mt.includes('mp4') || mt.includes('aac')) return 'AAC';
        if (mt.includes('wav')) return 'WAV';
        if (mt.includes('ogg') || mt.includes('vorbis')) return 'OGG';
        if (mt.includes('wma')) return 'WMA';
        if (mt.includes('aiff')) return 'AIFF';
        if (mt.includes('ape')) return 'APE';
        if (mt.includes('opus')) return 'OPUS';
        // fallback: parse codec description
        if (c.includes('flac')) return 'FLAC';
        if (c.includes('layer 3') || c.includes('mp3')) return 'MP3';
        if (c.includes('aac')) return 'AAC';
        if (c.includes('vorbis')) return 'OGG';
        if (c.includes('wav')) return 'WAV';
        if (c.includes('opus')) return 'OPUS';
        if (c.includes('wma')) return 'WMA';
        return '';
    },

    trackFormatClass(fmt) {
        const map = { MP3:'mp3', FLAC:'flac', AAC:'aac', ALAC:'alac', WAV:'wav', OGG:'ogg', WMA:'wma', OPUS:'opus', AIFF:'aiff', APE:'ape' };
        return map[fmt] ? `track-fmt-${map[fmt]}` : '';
    },

    // ─── Home Page ───────────────────────────────────────────
    async renderHome(el) {
        // Respect the same visibility rules as the sidebar (child/guest settings + global config)
        const pt = this._pageToggles || {};
        const showMovies     = (pt.movies ?? true) || (pt.tvshows ?? true);
        const showMusic      = pt.music       ?? true;
        const showMv         = pt.musicvideos ?? true;
        const showPics       = pt.pictures    ?? true;
        const showEbooks     = pt.ebooks      ?? true;
        const showAudioBooks = pt.audiobooks  ?? true;

        // Only fetch APIs for sections the current user is allowed to see
        const [recentTracks, recentMv, recentPics, recentEbooks, recentVideos, recentAudioBooks, continueWatching] = await Promise.all([
            showMusic      ? this.api('tracks/recent?limit=30')                    : Promise.resolve(null),
            showMv         ? this.api('musicvideos?sort=recent&limit=30')          : Promise.resolve(null),
            showPics       ? this.api('pictures?sort=recent&limit=30')             : Promise.resolve(null),
            showEbooks     ? this.api('ebooks?sort=recent&limit=30')               : Promise.resolve(null),
            showMovies     ? this.api('videos?sort=recent&limit=30&grouped=true')  : Promise.resolve(null),
            showAudioBooks ? this.api('audiobooks?sort=recent&limit=30')           : Promise.resolve(null),
            showMovies     ? this.api('videos/continue-watching').catch(() => [])  : Promise.resolve([])
        ]);

        const _hasTmdb      = !!(this._initCfg?.tmdbApiKey);
        const _hasWatchmode = !!(this._initCfg?.watchmodeApiKey);
        let html = `<div class="page-header"><h1>${this.t('page.home')}</h1>
            <div class="home-header-btns">
                <button class="insights-btn nr-home-btn${_hasTmdb ? '' : ' nr-home-btn--disabled'}"
                    ${_hasTmdb ? `onclick="App.navigate('newreleases')"` : `onclick="App._nrShowNoKeyTooltip(event,this)"`}
                    title="${_hasTmdb ? this.t('newreleases.title') : this.t('newreleases.noTmdbKeyBtn')}">
                    <svg class="insights-btn-icon"><use href="#icon-trending"/></svg>
                    <span>${this.t('newreleases.title')}</span>
                </button>
                <button class="insights-btn wtw-home-btn${_hasWatchmode ? '' : ' nr-home-btn--disabled'}"
                    ${_hasWatchmode ? `onclick="App.navigate('wheretowatch')"` : `onclick="App._wtwShowNoKeyTooltip(event,this)"`}
                    title="${this.t('watchmode.title')}">
                    <svg class="insights-btn-icon"><use href="#icon-search"/></svg>
                    <span>${this.t('watchmode.title')}</span>
                </button>
                <button class="insights-btn" onclick="App.navigate('insights')" title="Smart Insights - Personalized viewing stats">
                    <svg class="insights-btn-icon"><use href="#icon-bar-chart"/></svg>
                    <span>Smart Insights</span>
                </button>
            </div></div>`;

        const hasAny = (showMusic      && recentTracks?.length > 0)
                    || (showMv         && recentMv?.videos?.length > 0)
                    || (showPics       && recentPics?.pictures?.length > 0)
                    || (showEbooks     && recentEbooks?.ebooks?.length > 0)
                    || (showMovies     && recentVideos?.videos?.length > 0)
                    || (showAudioBooks && recentAudioBooks?.audioBooks?.length > 0);

        if (!hasAny) {
            html += `<div class="empty-state">
                <div class="empty-icon">&#127925;</div>
                <h2>${this.t('empty.welcome.title')}</h2>
                <p>${this.t('empty.welcome.desc')}</p>
                <button class="btn-primary" onclick="App.navigate('settings')">${this.t('btn.goToSettings')}</button>
            </div>`;
            el.innerHTML = html;
            return;
        }

        // ── Continue Watching (Movies/TV) ──
        if (showMovies && continueWatching && continueWatching.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-film"/></svg> ${this.t('section.continueWatching')}</h2>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll home-row-wide">`;
            continueWatching.forEach(v => {
                const isSeries = v.mediaType === 'tv' && v.seriesName;
                const title = isSeries ? v.seriesName : v.title;
                const imgSrc = v.posterPath ? `/videometa/${v.posterPath}` : (v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '');
                const hasPoster = !!v.posterPath;
                const pct = Math.min(100, Math.max(0, v.percentWatched || 0));
                const subtitle = isSeries
                    ? `S${String(v.season||'?').padStart(2,'0')}E${String(v.episode||'?').padStart(2,'0')} · ${this.esc(v.title)}`
                    : '';
                html += `<div class="mv-card home-card" onclick="App.openVideoDetail(${v.id})" data-video-id="${v.id}">
                    <div class="mv-card-thumb${hasPoster ? ' mv-card-poster' : ''}">
                        ${imgSrc
                            ? `<img src="${imgSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                        <div class="cw-progress-bar"><div class="cw-progress-fill" style="width:${pct}%"></div></div>
                        <button class="mv-card-play" onclick="event.stopPropagation(); App.openVideoDetail(${v.id})">&#9654;</button>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(title)}</div>
                        ${subtitle ? `<div class="mv-card-artist">${subtitle}</div>` : ''}
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Recently Added Movies & TV ──
        const videoList = recentVideos?.videos;
        if (showMovies && videoList && videoList.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-film"/></svg> ${this.t('section.recentMoviesTV')}</h2>
                    <button class="home-see-all" onclick="App.navigate('movies')">${this.t('btn.seeAll')} &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll home-row-wide">`;
            videoList.forEach(v => {
                const isSeries = v.type === 'series';
                const title = isSeries ? v.seriesName : v.title;
                const imgSrc = v.posterPath ? `/videometa/${v.posterPath}` : (v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '');
                const hasPoster = !!v.posterPath;
                const dur = this.formatDuration(v.duration);
                const typeLabel = v.mediaType === 'tv' ? 'TV' : v.mediaType === 'documentary' ? 'Doc' : v.mediaType === 'anime' ? 'Anime' : 'Movie';
                const subtitle = isSeries
                    ? `${v.seasonCount} Season${v.seasonCount !== 1 ? 's' : ''} · ${v.episodeCount} Episode${v.episodeCount !== 1 ? 's' : ''}`
                    : (v.year ? String(v.year) : '');
                const clickAction = isSeries
                    ? `App.openSeriesDetail('${this.esc(v.seriesName).replace(/'/g, "\\'")}')`
                    : `App.openVideoDetail(${v.id})`;
                const homeWatchedBadge = v.isWatched ? `<span class="mv-watched-badge"><svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>WATCHED</span>` : '';
                html += `<div class="mv-card home-card" onclick="${clickAction}" data-video-id="${v.id}" data-watched="${v.isWatched ? '1' : '0'}">
                    <div class="mv-card-thumb${hasPoster ? ' mv-card-poster' : ''}">
                        ${imgSrc
                            ? `<img src="${imgSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                        ${homeWatchedBadge}
                        <span class="mv-duration-badge">${dur}</span>
                        <span class="video-type-badge video-type-${v.mediaType}">${typeLabel}</span>
                        ${!isSeries ? `<button class="mv-card-play" onclick="event.stopPropagation(); App.playVideo(${v.id})">&#9654;</button>` : ''}
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(title)}</div>
                        <div class="mv-card-artist">${subtitle}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Recently Added Music ──
        if (showMusic && recentTracks && recentTracks.length > 0) {
            this._homeRecentTracks = recentTracks;
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-music"/></svg> ${this.t('section.recentMusic')}</h2>
                    <button class="home-see-all" onclick="App.navigate('music')">${this.t('btn.seeAll')} &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll">`;
            recentTracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                html += `<div class="song-card home-card" onclick="App.playHomeTrack(${i})">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`}
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playHomeTrack(${i})">&#9654;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta"><span>${this.esc(t.album)}</span><span>${dur}</span></div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Recently Added Music Videos ──
        const mvList = recentMv?.videos;
        if (showMv && mvList && mvList.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-video"/></svg> ${this.t('section.recentMusicVideos')}</h2>
                    <button class="home-see-all" onclick="App.navigate('musicvideos')">${this.t('btn.seeAll')} &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll home-row-wide">`;
            mvList.forEach(v => {
                const thumbSrc = v.thumbnailPath ? `/mvthumb/${v.thumbnailPath}` : '';
                const dur = this.formatDuration(v.duration);
                html += `<div class="mv-card home-card" onclick="App.openMvDetail(${v.id})" data-mv-id="${v.id}">
                    <div class="mv-card-thumb">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none">&#127909;</span>`
                            : `<span class="mv-card-placeholder">&#127909;</span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        <button class="mv-card-play" onclick="event.stopPropagation(); App.playMusicVideo(${v.id})">&#9654;</button>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${this.esc(v.artist)}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Recently Added Pictures ──
        const picList = recentPics?.pictures;
        if (showPics && picList && picList.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-image"/></svg> ${this.t('section.recentPictures')}</h2>
                    <button class="home-see-all" onclick="App.navigate('pictures')">${this.t('btn.seeAll')} &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll">`;
            picList.forEach(pic => {
                const thumbSrc = pic.thumbnailPath ? `/picthumb/${pic.thumbnailPath}` : `/api/picthumb/${pic.id}`;
                html += `<div class="picture-card home-card" onclick="App.openPictureViewer(${pic.id})" data-pic-id="${pic.id}">
                    <div class="picture-card-thumb">
                        <img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                        <span class="picture-card-placeholder" style="display:none">&#128247;</span>
                    </div>
                    <div class="picture-card-info">
                        <div class="picture-card-name">${this.esc(pic.fileName)}</div>
                        <div class="picture-card-meta">${pic.width}x${pic.height} &middot; ${this.formatSize(pic.sizeBytes)}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Recently Added eBooks ──
        const ebookList = recentEbooks?.ebooks;
        if (showEbooks && ebookList && ebookList.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-book"/></svg> ${this.t('section.recentEbooks')}</h2>
                    <button class="home-see-all" onclick="App.navigate('ebooks')">${this.t('btn.seeAll')} &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll">`;
            ebookList.forEach(book => {
                const formatBadge = book.format ? book.format.toLowerCase() : 'epub';
                const author = book.author || 'Unknown Author';
                html += `<div class="ebook-card home-card" onclick="App.openEBookDetail(${book.id})" data-ebook-id="${book.id}">
                    <div class="ebook-card-cover">
                        ${book.coverImage
                            ? `<img src="/ebookcover/${book.coverImage}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="ebook-card-placeholder" style="display:none">&#128214;</span>`
                            : `<span class="ebook-card-placeholder">&#128214;</span>`}
                        <span class="ebook-format-badge ebook-format-${formatBadge}">${book.format}</span>
                    </div>
                    <div class="ebook-card-info">
                        <div class="ebook-card-title">${this.esc(book.title)}</div>
                        <div class="ebook-card-author">${this.esc(author)}</div>
                        <div class="ebook-card-meta">
                            <span>${book.pageCount > 0 ? book.pageCount + ' pages' : book.format}</span>
                            <span>${this.formatSize(book.fileSize)}</span>
                        </div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Recently Added Audio Books ──
        const audioBookList = recentAudioBooks?.audioBooks;
        if (showAudioBooks && audioBookList && audioBookList.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-headphones"/></svg> ${this.t('section.recentAudioBooks')}</h2>
                    <button class="home-see-all" onclick="App.navigate('audiobooks')">${this.t('btn.seeAll')} &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll">`;
            audioBookList.forEach(book => {
                const fmt = (book.format || 'MP3').toUpperCase();
                const fmtClass = fmt === 'M4B' ? 'm4b' : 'mp3';
                const author = book.author || 'Unknown Author';
                const dur = book.duration > 0 ? this.formatDuration(Math.round(book.duration)) : fmt;
                html += `<div class="ebook-card audiobook-card home-card" onclick="App.openAudioBookDetail(${book.id})" data-audiobook-id="${book.id}">
                    <div class="ebook-card-cover">
                        ${book.coverImage
                            ? `<img src="/audiobookcover/${book.coverImage}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="ebook-card-placeholder" style="display:none">&#127911;</span>`
                            : `<span class="ebook-card-placeholder">&#127911;</span>`}
                        <span class="ebook-format-badge ebook-format-${fmtClass}">${fmt}</span>
                    </div>
                    <div class="ebook-card-info">
                        <div class="ebook-card-title">${this.esc(book.title)}</div>
                        <div class="ebook-card-author">${this.esc(author)}</div>
                        <div class="ebook-card-meta">
                            <span>${dur}</span>
                            <span>${this.formatSize(book.fileSize)}</span>
                        </div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        el.innerHTML = html;
    },

    homeRowScroll(btn, direction) {
        const row = btn.closest('.home-row');
        const scrollEl = row.querySelector('.home-row-scroll');
        if (!scrollEl) return;
        const cardWidth = scrollEl.querySelector('.home-card')?.offsetWidth || 170;
        const gap = 16;
        const visibleCards = Math.floor(scrollEl.clientWidth / (cardWidth + gap));
        const scrollAmount = (cardWidth + gap) * Math.max(visibleCards, 1);
        scrollEl.scrollBy({ left: direction * scrollAmount, behavior: 'smooth' });
    },

    // ─── Smart Insights Page ────────────────────────────────
    async renderInsights(el) {
        const data = await this.api('insights');
        if (!data) { el.innerHTML = this.emptyState(this.t('insights.title'), this.t('insights.loadError')); return; }

        let html = `<div class="insights-page">`;
        html += `<div class="page-header"><h1>${this.t('insights.title')}</h1></div>`;

        // ── Insight Messages ──
        if (data.insightMessages && data.insightMessages.length > 0) {
            html += `<div class="insights-section"><div class="insights-section-title">${this.t('insights.learnedAboutYou')}</div>`;
            data.insightMessages.forEach(m => {
                let text = '';
                if (m.key === 'completed') {
                    const parts = [];
                    if (m.movies > 0) parts.push(this.t('insights.msg.movies').replace('{0}', m.movies));
                    if (m.tvEpisodes > 0) parts.push(this.t('insights.msg.tvEpisodes').replace('{0}', m.tvEpisodes));
                    if (m.docs > 0) parts.push(this.t('insights.msg.docs').replace('{0}', m.docs));
                    text = this.t('insights.msg.completed').replace('{0}', parts.join(` ${this.t('insights.msg.and')} `));
                } else if (m.key === 'watchDays') {
                    text = this.t('insights.msg.watchDays').replace('{0}', m.days);
                } else if (m.key === 'watchHours') {
                    text = this.t('insights.msg.watchHours').replace('{0}', m.hours);
                } else if (m.key === 'topGenre') {
                    text = this.t('insights.msg.topGenre').replace('{0}', m.genre).replace('{1}', m.count);
                } else if (m.key === 'comfortTitle') {
                    text = this.t('insights.msg.comfortTitle').replace('{0}', m.title).replace('{1}', m.playCount);
                } else if (m.key === 'startWatching') {
                    text = this.t('insights.msg.startWatching');
                }
                if (text) html += `<div class="insight-message"><span class="insight-icon"><svg style="width:18px;height:18px;stroke:#a78bfa;fill:none;stroke-width:2"><use href="#icon-trending"/></svg></span><span>${this.esc(text)}</span></div>`;
            });
            html += `</div>`;
        }

        // ── Stats Grid ──
        html += `<div class="insights-section"><div class="insights-section-title">${this.t('insights.viewingStats')}</div>`;
        html += `<div class="insights-stats-grid">
            <div class="stat-card"><div class="stat-value">${data.totalWatched}</div><div class="stat-label">${this.t('insights.watched')}</div></div>
            <div class="stat-card"><div class="stat-value">${data.moviesWatched}</div><div class="stat-label">${this.t('insights.movies')}</div></div>
            <div class="stat-card"><div class="stat-value">${data.tvEpisodesWatched}</div><div class="stat-label">${this.t('insights.tvEpisodes')}</div></div>
            <div class="stat-card"><div class="stat-value">${data.totalWatchTimeHours}h</div><div class="stat-label">${this.t('insights.watchTime')}</div></div>
        </div>`;

        // ── Library progress bar ──
        if (data.library && data.library.total > 0) {
            const pct = Math.round(data.totalWatched / data.library.total * 100);
            html += `<div class="insights-progress-wrap">
                <div class="insights-progress-label">${this.t('insights.libraryProgress')}: ${data.totalWatched} / ${data.library.total} (${pct}%)</div>
                <div class="insights-progress-bar"><div class="insights-progress-fill" style="width:${pct}%"></div></div>
            </div>`;
        }
        html += `</div>`;

        // ── Favorite Genres ──
        if (data.favoriteGenres && data.favoriteGenres.length > 0) {
            html += `<div class="insights-section"><div class="insights-section-title">${this.t('insights.favoriteGenres')}</div><div class="insights-genre-tags">`;
            data.favoriteGenres.forEach(g => {
                html += `<span class="insights-genre-tag">${this.esc(g.name)} <small>(${g.count})</small></span>`;
            });
            html += `</div></div>`;
        }

        // ── Comfort Content (Most Replayed) ──
        if (data.comfortContent && data.comfortContent.length > 0) {
            html += `<div class="insights-section"><div class="insights-section-title">${this.t('insights.comfortContent')}</div>
                <p class="insights-section-desc">${this.t('insights.comfortContentDesc')}</p>
                <div class="insights-comfort-list">`;
            data.comfortContent.forEach(item => {
                const imgSrc = item.posterPath ? `/videometa/${item.posterPath}` : item.thumbnailPath ? `/videothumb/${item.thumbnailPath}` : '';
                const typeLabel = item.mediaType === 'tv' ? this.t('insights.tv') : item.mediaType === 'documentary' ? this.t('insights.doc') : item.mediaType === 'anime' ? 'Anime' : this.t('insights.movie');
                html += `<div class="insights-comfort-item" onclick="App.openVideoDetail(${item.id})">
                    <div class="insights-comfort-thumb">
                        ${imgSrc ? `<img src="${imgSrc}" loading="lazy" onerror="this.style.display='none'" alt="">` : ''}
                        <span class="video-type-badge video-type-${item.mediaType}">${typeLabel}</span>
                    </div>
                    <div class="insights-comfort-info">
                        <div class="insights-comfort-title">${this.esc(item.title)}</div>
                        <div class="insights-comfort-plays">${this.t('insights.playedTimes').replace('{0}', item.playCount)}</div>
                    </div>
                </div>`;
            });
            html += `</div></div>`;
        }

        // ── Recently Completed ──
        if (data.recentlyCompleted && data.recentlyCompleted.length > 0) {
            html += `<div class="insights-section"><div class="insights-section-title">${this.t('insights.recentlyWatched')}</div>
                <div class="home-row"><button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                <div class="home-row-scroll home-row-wide">`;
            data.recentlyCompleted.forEach(v => {
                const thumbSrc = v.posterPath ? `/videometa/${v.posterPath}` : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
                const typeLabel = v.mediaType === 'tv' ? this.t('insights.tv') : v.mediaType === 'documentary' ? this.t('insights.doc') : v.mediaType === 'anime' ? 'Anime' : this.t('insights.movie');
                const subtitle = v.year ? String(v.year) : '';
                html += `<div class="mv-card home-card" onclick="App.openVideoDetail(${v.id})">
                    <div class="mv-card-thumb">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                        <span class="video-type-badge video-type-${v.mediaType}">${typeLabel}</span>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${subtitle}</div>
                    </div>
                </div>`;
            });
            html += `</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>`;
        }

        // ── Top Rated Unwatched ──
        if (data.topRatedUnwatched && data.topRatedUnwatched.length > 0) {
            html += `<div class="insights-section"><div class="insights-section-title">${this.t('insights.topRatedUnwatched')}</div>
                <p class="insights-section-desc">${this.t('insights.topRatedUnwatchedDesc')}</p>
                <div class="home-row"><button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                <div class="home-row-scroll home-row-wide">`;
            data.topRatedUnwatched.forEach(v => {
                const imgSrc = v.posterPath ? `/videometa/${v.posterPath}` : (v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '');
                const hasPoster = !!v.posterPath;
                const typeLabel = v.mediaType === 'tv' ? this.t('insights.tv') : v.mediaType === 'documentary' ? this.t('insights.doc') : v.mediaType === 'anime' ? 'Anime' : this.t('insights.movie');
                const ratingBadge = v.rating ? `<span class="insights-rating-badge">${v.rating.toFixed(1)}</span>` : '';
                html += `<div class="mv-card home-card" onclick="App.openVideoDetail(${v.id})">
                    <div class="mv-card-thumb${hasPoster ? ' mv-card-poster' : ''}">
                        ${imgSrc
                            ? `<img src="${imgSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                        ${ratingBadge}
                        <span class="video-type-badge video-type-${v.mediaType}">${typeLabel}</span>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${v.year || ''} ${v.genre ? '&middot; ' + this.esc(v.genre.split(',')[0]) : ''}</div>
                    </div>
                </div>`;
            });
            html += `</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>`;
        }

        // ── Mood Explorer ──
        html += `<div class="insights-section">
            <div class="insights-section-title">${this.t('insights.howFeeling')}</div>
            <p class="insights-section-desc">${this.t('insights.howFeelingDesc')}</p>
            <div class="mood-grid" id="mood-grid"></div>
            <div id="mood-results" class="mood-results" style="display:none">
                <div class="mood-results-header">
                    <h3 id="mood-results-title">${this.t('insights.recommendations')}</h3>
                    <button class="mood-close-btn" onclick="document.getElementById('mood-results').style.display='none';document.querySelectorAll('.mood-card').forEach(c=>c.classList.remove('mood-active'))">&#10005;</button>
                </div>
                <div id="mood-results-grid" class="home-row-scroll home-row-wide" style="display:flex;gap:16px;overflow-x:auto;padding-bottom:8px"></div>
            </div>
        </div>`;

        html += `</div>`;
        el.innerHTML = html;

        // Load mood cards
        const moods = await this.api('insights/moods');
        const moodGrid = document.getElementById('mood-grid');
        if (moods && moodGrid) {
            moodGrid.innerHTML = moods.map(m => {
                const mName = this.t('insights.mood.' + m.key) || m.name;
                const mDesc = this.t('insights.moodDesc.' + m.key) || m.description;
                return `<div class="mood-card" data-mood="${m.key}" style="--mood-color:${m.color}" onclick="App.selectMood('${m.key}')">
                    <div class="mood-card-icon"><svg class="mood-svg-icon" style="--mood-color:${m.color}"><use href="#icon-${m.icon}"/></svg></div>
                    <div class="mood-card-name">${this.esc(mName)}</div>
                    <div class="mood-card-desc">${this.esc(mDesc)}</div>
                </div>`;
            }).join('');
        }
    },

    async selectMood(moodKey) {
        // Highlight selected mood
        document.querySelectorAll('.mood-card').forEach(c => c.classList.remove('mood-active'));
        const card = document.querySelector(`.mood-card[data-mood="${moodKey}"]`);
        if (card) card.classList.add('mood-active');

        const moodName = this.t('insights.mood.' + moodKey) || moodKey;
        const resultsEl = document.getElementById('mood-results');
        const gridEl = document.getElementById('mood-results-grid');
        const titleEl = document.getElementById('mood-results-title');
        resultsEl.style.display = 'block';
        titleEl.textContent = moodName + ' \u2014 ' + this.t('insights.recommendations');
        gridEl.innerHTML = '<div style="padding:40px;color:rgba(255,255,255,.5);text-align:center;width:100%">' + this.t('insights.loadingRecommendations') + '</div>';

        const data = await this.api(`insights/mood-recommendations?mood=${moodKey}`);
        if (!data || !data.recommendations || data.recommendations.length === 0) {
            gridEl.innerHTML = '<div style="padding:40px;color:rgba(255,255,255,.5);text-align:center;width:100%">' + this.t('insights.noRecommendations') + '</div>';
            return;
        }

        gridEl.innerHTML = data.recommendations.map(v => {
            const imgSrc = v.posterPath ? `/videometa/${v.posterPath}` : (v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '');
            const hasPoster = !!v.posterPath;
            const typeLabel = v.mediaType === 'tv' ? this.t('insights.tv') : v.mediaType === 'documentary' ? this.t('insights.doc') : v.mediaType === 'anime' ? 'Anime' : this.t('insights.movie');
            const ratingBadge = v.rating > 0 ? `<span class="insights-rating-badge">${v.rating.toFixed(1)}</span>` : '';
            return `<div class="mv-card home-card" onclick="App.openVideoDetail(${v.id})" style="min-width:160px">
                <div class="mv-card-thumb${hasPoster ? ' mv-card-poster' : ''}">
                    ${imgSrc
                        ? `<img src="${imgSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                           <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                        : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                    ${ratingBadge}
                    <span class="video-type-badge video-type-${v.mediaType}">${typeLabel}</span>
                </div>
                <div class="mv-card-info">
                    <div class="mv-card-title">${this.esc(v.title)}</div>
                    <div class="mv-card-artist">${v.year || ''} ${v.genre ? '&middot; ' + this.esc(v.genre.split(',')[0]) : ''}</div>
                </div>
            </div>`;
        }).join('');

        // Scroll mood results into view
        resultsEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    },

    // ─── Actors Page ─────────────────────────────────────────
    async renderActors(el) {
        this._actorsPage = 1;
        this._actorsSearch = '';
        const render = async () => {
            const params = `page=${this._actorsPage}&limit=60&search=${encodeURIComponent(this._actorsSearch)}`;
            const data = await this.api(`actors?${params}`);
            if (!data) { el.innerHTML = this.emptyState('Error', 'Could not load actors.'); return; }

            let html = `<div class="page-header" style="display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px;margin-bottom:20px">
                <h2 style="margin:0">${this.t('page.actors')}</h2>
                <div style="display:flex;align-items:center;gap:10px">
                    <span style="color:var(--text-secondary);font-size:0.85rem">${this.t('actors.total')}: ${data.total}</span>
                    <input type="text" id="actors-search" placeholder="${this.t('actors.search')}" value="${this._actorsSearch}"
                        style="padding:6px 12px;background:var(--bg-card);border:1px solid var(--border-color);border-radius:6px;color:var(--text-primary);font-size:0.9rem;width:200px">
                </div>
            </div>`;

            if (data.actors.length === 0 && !this._actorsSearch) {
                html += `<div class="empty-state" style="text-align:center;padding:60px 20px">
                    <svg style="width:64px;height:64px;stroke:var(--text-secondary);fill:none;stroke-width:1.5;margin-bottom:16px"><use href="#icon-users"/></svg>
                    <h2>${this.t('page.actors')}</h2>
                    <p style="color:var(--text-secondary);margin-bottom:20px">${this.t('actors.populateHint')}</p>
                    <button class="btn-primary" id="btn-populate-actors" style="padding:10px 24px;font-size:1rem">
                        <svg style="width:18px;height:18px;stroke:currentColor;fill:none;stroke-width:2;vertical-align:middle;margin-right:6px"><use href="#icon-refresh"/></svg>
                        ${this.t('actors.populate')}
                    </button>
                    <div id="populate-status" style="margin-top:16px;color:var(--text-secondary);font-size:0.9rem"></div>
                </div>`;
            } else if (data.actors.length === 0) {
                html += this.emptyState(this.t('page.actors'), this.t('actors.noResults') || 'No actors found.');
            } else {
                html += '<div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(130px,1fr));gap:16px">';
                data.actors.forEach(a => {
                    const photo = a.imageCached ? `/actorphoto/${a.imageCached}` : '';
                    html += `<div class="actor-card" onclick="App.openActorDetail(${a.id})" style="cursor:pointer;text-align:center">
                        <div style="width:100px;height:100px;border-radius:50%;margin:0 auto 8px;overflow:hidden;background:var(--bg-card);display:flex;align-items:center;justify-content:center;border:2px solid var(--border-color)">
                            ${photo
                                ? `<img src="${photo}" alt="${a.name}" style="width:100%;height:100%;object-fit:cover">`
                                : `<svg style="width:40px;height:40px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-users"/></svg>`}
                        </div>
                        <div style="font-size:0.85rem;font-weight:500;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${a.name}</div>
                        <div style="font-size:0.75rem;color:var(--text-secondary)">${a.movieCount} ${this.t('actors.movies')}</div>
                    </div>`;
                });
                html += '</div>';
            }

            // Pagination
            const totalPages = Math.ceil(data.total / 60);
            if (totalPages > 1) {
                html += '<div style="display:flex;justify-content:center;gap:8px;margin-top:20px">';
                if (this._actorsPage > 1)
                    html += `<button class="btn-secondary" onclick="App._actorsPage--;App._actorsRender()">&laquo; Prev</button>`;
                html += `<span style="padding:8px 12px;color:var(--text-secondary)">${this._actorsPage} / ${totalPages}</span>`;
                if (this._actorsPage < totalPages)
                    html += `<button class="btn-secondary" onclick="App._actorsPage++;App._actorsRender()">Next &raquo;</button>`;
                html += '</div>';
            }

            el.innerHTML = html;

            // Search handler
            let searchTimer;
            const searchInput = document.getElementById('actors-search');
            if (searchInput) {
                searchInput.addEventListener('input', () => {
                    clearTimeout(searchTimer);
                    searchTimer = setTimeout(() => {
                        this._actorsSearch = searchInput.value;
                        this._actorsPage = 1;
                        render();
                    }, 400);
                });
            }

            // Populate actors button
            const populateBtn = document.getElementById('btn-populate-actors');
            if (populateBtn) {
                populateBtn.addEventListener('click', async () => {
                    populateBtn.disabled = true;
                    populateBtn.textContent = this.t('actors.populating');
                    const statusEl = document.getElementById('populate-status');
                    if (statusEl) statusEl.textContent = this.t('actors.populatingHint');
                    const result = await this.apiPost('actors/populate');
                    if (result && result.success) {
                        if (statusEl) statusEl.textContent = `Done! ${result.total} actors found.`;
                        this.loadBadgeCounts();
                        setTimeout(() => render(), 1500);
                    } else {
                        if (statusEl) statusEl.textContent = result?.message || 'Failed to populate actors.';
                        populateBtn.disabled = false;
                        populateBtn.textContent = this.t('actors.populate');
                    }
                });
            }
        };
        this._actorsRender = render;
        await render();
    },

    async openActorDetail(id) {
        const content = document.getElementById('main-content');
        content.innerHTML = `<div class="loading-spinner" style="text-align:center;padding:60px"><div class="spinner"></div><p>${this.t('actors.loading')}</p></div>`;

        const actor = await this.api(`actors/${id}`);
        if (!actor) { content.innerHTML = this.emptyState('Error', 'Actor not found.'); return; }

        const photo = actor.imageCached ? `/actorphoto/${actor.imageCached}` : '';

        let html = `<div style="margin-bottom:20px">
            <button class="btn-secondary" onclick="App.navigate('actors')">
                <svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2"><use href="#icon-arrow-left"/></svg>
                ${this.t('actors.backToActors')}
            </button>
        </div>`;

        // Actor header
        html += `<div style="display:flex;gap:24px;align-items:flex-start;flex-wrap:wrap;margin-bottom:30px">
            <div style="width:150px;height:150px;border-radius:50%;overflow:hidden;flex-shrink:0;background:var(--bg-card);display:flex;align-items:center;justify-content:center;border:3px solid var(--border-color)">
                ${photo
                    ? `<img src="${photo}" alt="${actor.name}" style="width:100%;height:100%;object-fit:cover">`
                    : `<svg style="width:60px;height:60px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-users"/></svg>`}
            </div>
            <div style="flex:1;min-width:250px">
                <h2 style="margin:0 0 4px">${actor.name}</h2>
                <div style="color:var(--text-secondary);font-size:0.9rem;margin-bottom:8px">${actor.knownForDepartment || 'Acting'}</div>`;

        if (actor.birthday) {
            html += `<div style="color:var(--text-secondary);font-size:0.85rem">
                ${this.t('actors.born')}: ${actor.birthday}${actor.placeOfBirth ? ` — ${actor.placeOfBirth}` : ''}
            </div>`;
        }
        if (actor.deathday) {
            html += `<div style="color:var(--text-secondary);font-size:0.85rem">${this.t('actors.died')}: ${actor.deathday}</div>`;
        }

        html += `<div style="margin-top:8px;display:flex;gap:12px;flex-wrap:wrap">
                <span style="background:var(--accent);color:#fff;padding:4px 10px;border-radius:12px;font-size:0.8rem">${actor.movieCount} ${this.t('actors.inLibrary')}</span>
            </div>`;

        // Biography
        if (actor.biography) {
            const bio = actor.biography.replace(/\n/g, '<br>');
            const shortBio = bio.length > 500 ? bio.substring(0, 500) + '...' : bio;
            html += `<div style="margin-top:12px;font-size:0.9rem;color:var(--text-secondary);line-height:1.5">
                <span id="actor-bio-short">${shortBio}</span>
                <span id="actor-bio-full" style="display:none">${bio}</span>`;
            if (bio.length > 500) {
                html += ` <a href="#" id="actor-bio-toggle" style="color:var(--accent)" onclick="event.preventDefault();
                    const s=document.getElementById('actor-bio-short'),f=document.getElementById('actor-bio-full'),t=document.getElementById('actor-bio-toggle');
                    if(s.style.display!=='none'){s.style.display='none';f.style.display='inline';t.textContent='${this.t('actors.readLess')}';}
                    else{s.style.display='inline';f.style.display='none';t.textContent='${this.t('actors.readMore')}';}">${this.t('actors.readMore')}</a>`;
            }
            html += '</div>';
        }

        html += `</div></div>`;

        // In Your Library section
        html += `<div style="margin-bottom:30px">
            <h3 style="margin-bottom:12px">${this.t('actors.inYourLibrary')}</h3>
            <div id="actor-library-movies" style="display:flex;gap:12px;overflow-x:auto;padding-bottom:8px">
                <div class="spinner" style="margin:20px auto"></div>
            </div>
        </div>`;

        // Known For section
        html += `<div style="margin-bottom:30px">
            <h3 style="margin-bottom:12px">${this.t('actors.knownFor')}</h3>
            <div id="actor-knownfor" style="display:flex;gap:12px;overflow-x:auto;padding-bottom:8px">
                <div class="spinner" style="margin:20px auto"></div>
            </div>
        </div>`;

        content.innerHTML = html;

        // Load library movies async
        const movies = await this.api(`actors/${id}/movies`);
        const libraryEl = document.getElementById('actor-library-movies');
        if (libraryEl) {
            if (movies && movies.length > 0) {
                libraryEl.innerHTML = movies.map(m => {
                    const poster = m.posterPath ? `/videometa/${m.posterPath}` : '';
                    return `<div style="flex-shrink:0;width:140px;cursor:pointer;text-align:center" onclick="${m.mediaType === 'tv' ? `App.openSeriesDetail('${(m.seriesName || m.title).replace(/'/g, "\\'")}')` : `App.openVideoDetail(${m.id})`}">
                        <div style="width:140px;height:200px;border-radius:8px;overflow:hidden;background:var(--bg-card);margin-bottom:6px;border:1px solid var(--border-color)">
                            ${poster
                                ? `<img src="${poster}" style="width:100%;height:100%;object-fit:cover">`
                                : `<div style="display:flex;align-items:center;justify-content:center;height:100%"><svg style="width:40px;height:40px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></div>`}
                        </div>
                        <div style="font-size:0.8rem;font-weight:500;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${m.seriesName || m.title}</div>
                        <div style="font-size:0.7rem;color:var(--text-secondary)">${m.characterName ? `as ${m.characterName}` : ''} ${m.year ? `(${m.year})` : ''}${m.episodeCount > 0 ? ` · ${m.episodeCount} ep.` : ''}</div>
                    </div>`;
                }).join('');
            } else {
                libraryEl.innerHTML = `<div style="color:var(--text-secondary);font-size:0.9rem;padding:20px">${this.t('actors.noResults') || 'No movies found.'}</div>`;
            }
        }

        // Load known-for credits async
        const knownfor = await this.api(`actors/${id}/knownfor`);
        this._kfItems = knownfor || [];
        const kfEl = document.getElementById('actor-knownfor');
        if (kfEl) {
            if (knownfor && knownfor.length > 0) {
                kfEl.innerHTML = knownfor.map((k, idx) => {
                    const poster = k.poster ? `/actorphoto/${k.poster}` : '';
                    return `<div style="flex-shrink:0;width:140px;text-align:center;cursor:pointer" onclick="App._kfOpenDetail(${idx})">
                        <div style="width:140px;height:200px;border-radius:8px;overflow:hidden;background:var(--bg-card);margin-bottom:6px;border:1px solid var(--border-color);transition:transform .15s,box-shadow .15s" onmouseenter="this.style.transform='scale(1.04)';this.style.boxShadow='0 6px 20px rgba(0,0,0,.5)'" onmouseleave="this.style.transform='';this.style.boxShadow=''">
                            ${poster
                                ? `<img src="${poster}" style="width:100%;height:100%;object-fit:cover">`
                                : `<div style="display:flex;align-items:center;justify-content:center;height:100%"><svg style="width:40px;height:40px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></div>`}
                        </div>
                        <div style="font-size:0.8rem;font-weight:500;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${k.title}</div>
                        <div style="font-size:0.7rem;color:var(--text-secondary)">${k.mediaType === 'tv' ? 'TV' : k.mediaType === 'anime' ? 'Anime' : 'Movie'} ${k.year ? `(${k.year})` : ''}</div>
                    </div>`;
                }).join('');
            } else {
                kfEl.innerHTML = `<div style="color:var(--text-secondary);font-size:0.9rem;padding:20px">${this.t('actors.noResults') || 'No credits found.'}</div>`;
            }
        }
    },

    async _kfOpenDetail(idx) {
        const item = this._kfItems?.[idx];
        if (!item) return;

        const posterSrc = item.poster ? `/actorphoto/${item.poster}` : '';
        const tmdbUrl   = `https://www.themoviedb.org/${item.mediaType === 'tv' ? 'tv' : 'movie'}/${item.tmdbId}`;
        const typeLabel = item.mediaType === 'tv' ? this.t('filter.tvShows') : this.t('filter.movies');

        const overlay = document.createElement('div');
        overlay.className = 'nr-detail-overlay';
        overlay.innerHTML = `
            <div class="nr-detail-modal" onclick="event.stopPropagation()">
                <button class="nr-detail-close" onclick="this.closest('.nr-detail-overlay').remove()">✕</button>
                <div class="nr-detail-body">
                    <div class="nr-detail-poster">
                        ${posterSrc
                            ? `<img src="${this.esc(posterSrc)}" alt="" onerror="this.style.display='none'">`
                            : `<span class="nr-card-ph" style="height:200px"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                    </div>
                    <div class="nr-detail-info">
                        <h2 class="nr-detail-title">${this.esc(item.title)}</h2>
                        <div class="nr-detail-meta" id="kf-detail-meta">
                            ${item.year ? `<span>${item.year}</span>` : ''}
                            <span class="nr-detail-type">${typeLabel}</span>
                        </div>
                        <p class="nr-detail-overview" id="kf-detail-overview" style="color:var(--text-secondary);font-size:0.85rem">Loading…</p>
                        <div class="nr-detail-actions">
                            <button class="btn-primary" onclick="App._kfSearchLibrary('${this.esc(item.title.replace(/'/g,"\\x27"))}','${item.mediaType}',this)">${this.t('newreleases.findInLibrary')}</button>
                            <button class="nr-trailer-btn" onclick="App._nrOpenTrailer(${item.tmdbId},'${item.mediaType}',this)">▶ ${this.t('newreleases.trailer')}</button>
                            <a class="nr-tmdb-link" href="${tmdbUrl}" target="_blank" rel="noopener">${this.t('newreleases.openTmdb')}</a>
                        </div>
                    </div>
                </div>
            </div>`;
        overlay.addEventListener('click', () => overlay.remove());
        document.body.appendChild(overlay);

        // Fetch overview + rating in background
        const details = await this.api(`tmdb/details?type=${item.mediaType}&id=${item.tmdbId}`);
        const metaEl  = overlay.querySelector('#kf-detail-meta');
        const ovEl    = overlay.querySelector('#kf-detail-overview');
        if (!overlay.isConnected) return; // dismissed while loading
        if (details) {
            if (metaEl) {
                const rating = details.rating ? details.rating.toFixed(1) : null;
                metaEl.innerHTML =
                    (item.year ? `<span>${item.year}</span>` : '') +
                    (rating    ? `<span>⭐ ${rating}</span>` : '') +
                    (details.votes > 0 ? `<span>${details.votes.toLocaleString()} ${this.t('newreleases.votes')}</span>` : '') +
                    `<span class="nr-detail-type">${typeLabel}</span>`;
            }
            if (ovEl) ovEl.textContent = details.overview || '';
        } else if (ovEl) {
            ovEl.textContent = '';
        }
    },

    async _kfSearchLibrary(title, mediaType, btn) {
        const orig = btn.textContent;
        btn.disabled    = true;
        btn.textContent = this.t('player.loading');

        const mtQ = mediaType ? `&mediaType=${mediaType}` : '';
        const stripped = title.replace(/\s*[:\u2013\u2014]\s*.+$/, '').trim();
        const trySearch = async (q) => {
            const d = await this.api(`videos?search=${encodeURIComponent(q)}${mtQ}&limit=5`);
            return (d && d.videos && d.videos.length > 0) ? d : null;
        };

        let data = await trySearch(title);
        if (!data && stripped && stripped !== title) data = await trySearch(stripped);
        if (!data) {
            const words = title.split(/\s+/).slice(0, 3).join(' ');
            if (words !== title && words !== stripped) data = await trySearch(words);
        }

        btn.disabled    = false;
        btn.textContent = orig;
        if (!data) {
            btn.textContent = this.t('newreleases.notInLibrary');
            setTimeout(() => { btn.textContent = orig; }, 2500);
            return;
        }
        const v = data.videos[0];
        btn.closest('.nr-detail-overlay')?.remove();
        this.openVideoDetail(v.id);
    },

    // ─── Analysis Page ──────────────────────────────────────
    async renderAnalysis(el) {
        const data = await this.api('analysis');
        if (!data) { el.innerHTML = this.emptyState('Error', 'Could not load analysis data.'); return; }
        const t = data.totals;

        // ── Color palette per media type ──
        const C = {
            music:    '#1db954',  // green (Spotify-inspired)
            movies:   '#e67e22',  // warm orange
            mv:       '#9b59b6',  // purple
            pictures: '#3498db',  // blue
            ebooks:   '#e74c3c',  // red
            total:    '#2ecc71',  // bright green for totals
        };

        // Helper: render horizontal bar chart with distinct colors per bar
        const barColors = ['#1db954','#e67e22','#3498db','#9b59b6','#e74c3c','#f1c40f','#1abc9c','#e84393'];
        const barChart = (items, total, _color) => {
            if (!items || items.length === 0) return '<div style="color:var(--text-muted);font-size:13px;padding:8px 0">No data</div>';
            const max = Math.max(...items.map(i => i.count));
            // When no total is supplied (e.g. multi-track counts), sum all items
            const effectiveTotal = (total != null && total > 0) ? total : items.reduce((s, i) => s + i.count, 0);
            return items.slice(0, 8).map((i, idx) => {
                const pct = effectiveTotal > 0 ? (i.count / effectiveTotal * 100).toFixed(1) : 0;
                const barW = max > 0 ? (i.count / max * 100) : 0;
                const c = barColors[idx % barColors.length];
                return `<div class="an-bar-row">
                    <span class="an-bar-label">${this.esc(i.name)}</span>
                    <div class="an-bar-track"><div class="an-bar-fill" style="width:${barW}%;background:${c}"></div></div>
                    <span class="an-bar-value">${i.count.toLocaleString()} <span class="an-bar-pct">(${pct}%)</span></span>
                </div>`;
            }).join('');
        };

        // Helper: render SVG donut pie chart
        const pieChart = (items, getValue, fmtValue) => {
            if (!items || items.length === 0) return '<div style="color:var(--text-muted);font-size:13px;padding:8px 0">No data</div>';
            const pieColors = ['#1db954','#e67e22','#3498db','#9b59b6','#e74c3c','#f1c40f','#1abc9c','#e84393'];
            const slices = items.slice(0, 8);
            const totalVal = slices.reduce((s, i) => s + getValue(i), 0);
            if (totalVal === 0) return '<div style="color:var(--text-muted);font-size:13px;padding:8px 0">No data</div>';
            const cx = 80, cy = 80, r = 70, innerR = 30;
            let paths = '';
            let startAngle = -Math.PI / 2;
            const sliceColors = slices.map((item, idx) => item.color || pieColors[idx % pieColors.length]);
            slices.forEach((item, idx) => {
                const val = getValue(item);
                const fraction = val / totalVal;
                const angle = fraction * 2 * Math.PI;
                const endAngle = startAngle + angle;
                const color = sliceColors[idx];
                if (Math.abs(fraction - 1) < 0.0001) {
                    paths += `<circle cx="${cx}" cy="${cy}" r="${r}" fill="${color}"><title>${this.esc(item.name)}: ${fmtValue(val)} (100%)</title></circle>`;
                } else {
                    const x1 = cx + r * Math.cos(startAngle);
                    const y1 = cy + r * Math.sin(startAngle);
                    const x2 = cx + r * Math.cos(endAngle);
                    const y2 = cy + r * Math.sin(endAngle);
                    const largeArc = angle > Math.PI ? 1 : 0;
                    const pct = (fraction * 100).toFixed(1);
                    paths += `<path d="M${cx},${cy} L${x1.toFixed(2)},${y1.toFixed(2)} A${r},${r} 0 ${largeArc},1 ${x2.toFixed(2)},${y2.toFixed(2)} Z" fill="${color}"><title>${this.esc(item.name)}: ${fmtValue(val)} (${pct}%)</title></path>`;
                }
                startAngle = endAngle;
            });
            paths += `<circle cx="${cx}" cy="${cy}" r="${innerR}" fill="var(--bg-surface)"/>`;
            const legend = slices.map((item, idx) => {
                const val = getValue(item);
                const pct = totalVal > 0 ? (val / totalVal * 100).toFixed(1) : 0;
                const color = sliceColors[idx];
                return `<span class="an-legend-item"><span class="an-legend-dot" style="background:${color}"></span>${this.esc(item.name)}: ${fmtValue(val)} (${pct}%)</span>`;
            }).join('');
            return `<div class="an-pie-container"><svg class="an-pie-svg" viewBox="0 0 160 160">${paths}</svg><div class="an-stacked-legend">${legend}</div></div>`;
        };

        // Helper: render compact 2-column stats table
        const statsTable = (rows, color) => {
            const body = rows.map(([label, value]) =>
                `<tr><td class="an-tbl-label">${label}</td><td class="an-tbl-value" style="color:${color}">${value}</td></tr>`
            ).join('');
            return `<div class="an-panel an-stats-panel"><table class="an-stats-table"><tbody>${body}</tbody></table></div>`;
        };

        // Helper: render ranked list
        const medalIcons = ['medal-gold', 'medal-silver', 'medal-bronze'];
        const rankedList = (items, icon, color) => {
            if (!items || items.length === 0) return '<div style="color:var(--text-muted);font-size:13px;padding:8px 0">No data</div>';
            return items.slice(0, 8).map((item, i) => `<div class="an-rank-row">
                ${i < 3
                    ? `<svg class="an-medal-icon"><use href="#icon-${medalIcons[i]}"/></svg>`
                    : `<span class="an-rank-num">${i + 1}</span>`}
                <svg class="an-rank-icon"><use href="#icon-${icon}"/></svg>
                <span class="an-rank-name">${this.esc(item.name)}</span>
                <span class="an-rank-count">${item.count.toLocaleString()}</span>
            </div>`).join('');
        };

        let html = `<div class="page-header" style="display:flex;justify-content:space-between;align-items:flex-start;flex-wrap:wrap;gap:12px">
            <h1>${this.t('page.analysis')}</h1>
            <div style="display:flex;gap:8px">
                <a href="/api/export/csv" download class="an-export-btn">
                    <svg class="an-export-icon"><use href="#icon-download"/></svg> ${this.t('analysis.exportCsv')}
                </a>
                <a href="/api/export/html" download class="an-export-btn">
                    <svg class="an-export-icon"><use href="#icon-download"/></svg> ${this.t('analysis.exportHtml')}
                </a>
            </div>
        </div>`;

        // ════════════════ LIBRARY OVERVIEW ════════════════
        html += `<div class="section-title"><svg class="an-section-icon"><use href="#icon-bar-chart"/></svg> ${this.t('analysis.libraryOverview')}</div>`;
        html += `<div class="an-panel an-stats-panel"><table class="an-stats-table an-stats-table--ov">
            <thead><tr><th>Category</th><th>Items</th><th>Size</th></tr></thead>
            <tbody>
            <tr class="an-tbl-total"><td>${this.t('analysis.totalItems')}</td><td>${t.totalItems.toLocaleString()}</td><td>${this.formatSize(t.totalSize)}</td></tr>
            ${t.totalTracks > 0 ? `<tr><td style="color:${C.music}">${this.t('analysis.musicTracks')}</td><td>${t.totalTracks.toLocaleString()}</td><td>${this.formatSize(t.totalMusicSize)}</td></tr>` : ''}
            ${t.totalVideos > 0 ? `<tr><td style="color:${C.movies}">${this.t('analysis.moviesTv')}</td><td>${t.totalVideos.toLocaleString()}</td><td>${this.formatSize(t.totalVideoSize)}</td></tr>` : ''}
            ${t.totalMv > 0 ? `<tr><td style="color:${C.mv}">${this.t('analysis.musicVideos')}</td><td>${t.totalMv.toLocaleString()}</td><td>${this.formatSize(t.totalMvSize)}</td></tr>` : ''}
            ${t.totalPictures > 0 ? `<tr><td style="color:${C.pictures}">${this.t('analysis.pictures')}</td><td>${t.totalPictures.toLocaleString()}</td><td>${this.formatSize(t.totalPicSize)}</td></tr>` : ''}
            ${t.totalEbooks > 0 ? `<tr><td style="color:${C.ebooks}">${this.t('analysis.ebooks')}</td><td>${t.totalEbooks.toLocaleString()}</td><td>${this.formatSize(t.totalEbookSize)}</td></tr>` : ''}
            </tbody>
        </table></div>`;

        // Storage breakdown (stacked bar + list)
        const storageItems = [
            { name: 'Music', size: t.totalMusicSize, color: C.music },
            { name: 'Movies & TV', size: t.totalVideoSize, color: C.movies },
            { name: 'Music Videos', size: t.totalMvSize, color: C.mv },
            { name: 'Pictures', size: t.totalPicSize, color: C.pictures },
            { name: 'eBooks', size: t.totalEbookSize, color: C.ebooks }
        ].filter(s => s.size > 0);
        if (storageItems.length > 0) {
            html += `<div class="an-panel"><h3 class="an-panel-title"><svg class="an-section-icon"><use href="#icon-folder"/></svg> ${this.t('analysis.storageBreakdown')}</h3>`;
            html += pieChart(storageItems, s => s.size, v => this.formatSize(v));
            html += `</div>`;
        }

        // ════════════════ QUALITY ANALYSIS ════════════════
        if (t.totalVideos > 0 || t.totalMv > 0) {
            html += `<div class="section-title"><svg class="an-section-icon"><use href="#icon-video"/></svg> ${this.t('analysis.qualityAnalysis')}</div>`;
            html += '<div class="an-panels-row">';

            if (t.totalVideos > 0) {
                html += `<div class="an-panel"><h3 class="an-panel-title" style="color:${C.movies}">${this.t('analysis.moviesTvQuality')}</h3>
                    <div class="an-quality-meter"><div class="an-quality-fill" style="width:${t.hdRatioVideos}%;background:linear-gradient(90deg,${C.movies},${C.movies}cc)"></div><span class="an-quality-label">${t.hdRatioVideos}% ${this.t('analysis.hd')}</span></div>
                    <div class="an-mini-stats">
                        <span>${t.totalMovies} ${this.t('analysis.movies')}</span><span>${t.totalTvEpisodes} ${this.t('analysis.tvEpisodes')}</span>
                        ${t.videoNeedsOpt > 0 ? `<span style="color:var(--warning)">${t.videoNeedsOpt} ${this.t('analysis.needOptimization')}</span>` : ''}
                    </div>
                    ${pieChart(data.videos.resolutions, r => r.count, v => v.toLocaleString())}
                </div>`;
            }

            if (t.totalMv > 0) {
                html += `<div class="an-panel"><h3 class="an-panel-title" style="color:${C.mv}">${this.t('analysis.musicVideosQuality')}</h3>
                    <div class="an-quality-meter"><div class="an-quality-fill" style="width:${t.hdRatioMv}%;background:linear-gradient(90deg,${C.mv},${C.mv}cc)"></div><span class="an-quality-label">${t.hdRatioMv}% ${this.t('analysis.hd')}</span></div>
                    <div class="an-mini-stats">
                        <span>${t.totalMv} ${this.t('analysis.videos')}</span><span>${this.formatDuration(t.totalMvDuration)}</span>
                        ${t.mvNeedsOpt > 0 ? `<span style="color:var(--warning)">${t.mvNeedsOpt} ${this.t('analysis.needOptimization')}</span>` : ''}
                    </div>
                    ${pieChart(data.musicVideos.resolutions, r => r.count, v => v.toLocaleString())}
                </div>`;
            }

            html += '</div>';
        }

        // ════════════════ MUSIC ANALYSIS ════════════════
        if (t.totalTracks > 0) {
            html += `<div class="section-title"><svg class="an-section-icon" style="stroke:${C.music}"><use href="#icon-music"/></svg> Music Analysis</div>`;
            html += statsTable([
                ['Tracks', t.totalTracks.toLocaleString()],
                ['Albums', t.totalAlbums.toLocaleString()],
                ['Artists', t.totalArtists.toLocaleString()],
                ['Total Duration', this.formatDuration(t.totalMusicDuration)],
                ['Storage', this.formatSize(t.totalMusicSize)],
            ], C.music);

            html += '<div class="an-panels-row">';
            html += `<div class="an-panel"><h3 class="an-panel-title">Top Artists by Tracks</h3>${rankedList(data.music.topArtists, 'mic', C.music)}</div>`;
            html += `<div class="an-panel"><h3 class="an-panel-title">Top Genres</h3>${barChart(data.music.genres, t.totalTracks, C.music)}</div>`;
            html += '</div>';

            html += '<div class="an-panels-row">';
            html += `<div class="an-panel"><h3 class="an-panel-title">Audio Formats & Bitrate</h3>
                ${barChart(data.music.formats, t.totalTracks, C.music)}
                <div style="margin-top:12px;padding-top:12px;border-top:1px solid rgba(255,255,255,.06)">
                ${barChart(data.music.bitrates, t.totalTracks, C.music)}
                </div>
            </div>`;
            if (data.music.sampleRates && data.music.sampleRates.length > 0) {
                html += `<div class="an-panel"><h3 class="an-panel-title">Sample Rates</h3>${barChart(data.music.sampleRates, t.totalTracks, C.music)}</div>`;
            } else {
                html += '<div class="an-panel"></div>';
            }
            html += '</div>';
        }

        // ════════════════ MOVIES & TV ANALYSIS ════════════════
        if (t.totalVideos > 0) {
            html += `<div class="section-title"><svg class="an-section-icon" style="stroke:${C.movies}"><use href="#icon-film"/></svg> Movies & TV Analysis</div>`;
            html += statsTable([
                ['Total', t.totalVideos.toLocaleString()],
                ['Movies', t.totalMovies.toLocaleString()],
                ['TV Episodes', t.totalTvEpisodes.toLocaleString()],
                ['Total Duration', this.formatDuration(t.totalVideoDuration)],
                ['Storage', this.formatSize(t.totalVideoSize)],
            ], C.movies);

            html += '<div class="an-panels-row">';
            html += `<div class="an-panel"><h3 class="an-panel-title">Formats & Codecs</h3>
                ${barChart(data.videos.formats, t.totalVideos, C.movies)}
                <div style="margin-top:12px;padding-top:12px;border-top:1px solid rgba(255,255,255,.06)">
                ${barChart(data.videos.codecs, t.totalVideos, C.movies)}
                </div>
            </div>`;
            if (data.videos.genres && data.videos.genres.length > 0) {
                html += `<div class="an-panel"><h3 class="an-panel-title">Genres</h3>${barChart(data.videos.genres, t.totalVideos, C.movies)}</div>`;
            } else {
                html += '<div class="an-panel"></div>';
            }
            html += '</div>';

            // HDR row
            if (t.totalHdrVideos > 0 && data.videos.hdrFormats && data.videos.hdrFormats.length > 0) {
                const hdrPct = t.totalVideos > 0 ? Math.round(t.totalHdrVideos * 100 / t.totalVideos) : 0;
                html += '<div class="an-panels-row">';
                html += `<div class="an-panel"><h3 class="an-panel-title">HDR Content</h3>
                    <div class="an-quality-meter"><div class="an-quality-fill" style="width:${hdrPct}%;background:linear-gradient(90deg,rgba(243,156,18,.9),rgba(230,81,0,.9))"></div><span class="an-quality-label">${hdrPct}% HDR</span></div>
                    <div style="margin-top:10px">${barChart(data.videos.hdrFormats, t.totalVideos, 'rgba(243,156,18,.85)')}</div>
                    ${t.totalDolbyVisionVideos > 0 ? `<div class="an-mini-stats" style="margin-top:8px"><span style="color:rgba(77,166,255,.9)">&#9679; Dolby Vision: ${t.totalDolbyVisionVideos}</span></div>` : ''}
                </div>`;
                html += '<div class="an-panel"></div>';
                html += '</div>';
            }
        }

        // ════════════════ MUSIC VIDEOS ANALYSIS ════════════════
        if (t.totalMv > 0) {
            html += `<div class="section-title"><svg class="an-section-icon" style="stroke:${C.mv}"><use href="#icon-video"/></svg> Music Videos Analysis</div>`;
            html += statsTable([
                ['Videos', t.totalMv.toLocaleString()],
                ['Total Duration', this.formatDuration(t.totalMvDuration)],
                ['Storage', this.formatSize(t.totalMvSize)],
            ], C.mv);

            html += '<div class="an-panels-row">';
            html += `<div class="an-panel"><h3 class="an-panel-title">Top Artists by Videos</h3>${rankedList(data.musicVideos.topArtists, 'mic', C.mv)}</div>`;
            html += `<div class="an-panel"><h3 class="an-panel-title">Video Formats</h3>${barChart(data.musicVideos.formats, t.totalMv, C.mv)}</div>`;
            html += '</div>';
        }

        // ════════════════ PICTURES & EBOOKS (side by side when both exist) ════════════════
        const hasPics = t.totalPictures > 0;
        const hasEbooks = t.totalEbooks > 0;

        if (hasPics || hasEbooks) {
            if (hasPics) {
                html += `<div class="section-title"><svg class="an-section-icon" style="stroke:${C.pictures}"><use href="#icon-image"/></svg> Pictures Analysis</div>`;
                html += statsTable([
                    ['Pictures', t.totalPictures.toLocaleString()],
                    ['Storage', this.formatSize(t.totalPicSize)],
                ], C.pictures);
                html += '<div class="an-panels-row">';
                html += `<div class="an-panel"><h3 class="an-panel-title">Formats</h3>${pieChart(data.pictures.formats, r => r.count, v => v.toLocaleString())}</div>`;
                html += `<div class="an-panel"><h3 class="an-panel-title">Resolutions</h3>${pieChart(data.pictures.resolutions, r => r.count, v => v.toLocaleString())}</div>`;
                html += '</div>';
                if (data.pictures.categories && data.pictures.categories.length > 0) {
                    html += '<div class="an-panels-row">';
                    html += `<div class="an-panel"><h3 class="an-panel-title">Top Categories</h3>${rankedList(data.pictures.categories, 'folder', C.pictures)}</div>`;
                    html += '<div class="an-panel"></div>';
                    html += '</div>';
                }
            }

            if (hasEbooks) {
                html += `<div class="section-title"><svg class="an-section-icon" style="stroke:${C.ebooks}"><use href="#icon-book"/></svg> eBooks Analysis</div>`;
                html += statsTable([
                    ['eBooks', t.totalEbooks.toLocaleString()],
                    ['Storage', this.formatSize(t.totalEbookSize)],
                ], C.ebooks);
                html += '<div class="an-panels-row">';
                html += `<div class="an-panel"><h3 class="an-panel-title">Formats</h3>${pieChart(data.ebooks.formats, r => r.count, v => v.toLocaleString())}</div>`;
                html += `<div class="an-panel"><h3 class="an-panel-title">Top Authors by Titles</h3>${rankedList(data.ebooks.topAuthors, 'book', C.ebooks)}</div>`;
                html += '</div>';
                if (data.ebooks.categories && data.ebooks.categories.length > 0) {
                    html += '<div class="an-panels-row">';
                    html += `<div class="an-panel"><h3 class="an-panel-title">Categories</h3>${rankedList(data.ebooks.categories, 'folder', C.ebooks)}</div>`;
                    html += '<div class="an-panel"></div>';
                    html += '</div>';
                }
            }
        }

        // ════════════════ DEEP MEDIA ANALYSIS ════════════════
        const C_deep = '#00bcd4';
        const deepStatus = await this.api('analysis/deep/status');
        const ffmpegOk = this._initCfg?.ffmpegAvailable;

        html += `<div class="section-title"><svg class="an-section-icon" style="stroke:${C_deep}"><use href="#icon-activity"/></svg> ${this.t('analysis.deepScan')}</div>`;

        if (!ffmpegOk) {
            html += `<div class="an-panel"><p style="color:var(--text-secondary);margin:0">${this.t('analysis.deepScanNoFfprobe')}</p></div>`;
        } else {
            // Progress card — always shown (buttons work even before server restart)
            const pct = (deepStatus && deepStatus.total > 0) ? Math.round(deepStatus.analyzed / deepStatus.total * 100) : 0;
            const statusLabel = deepStatus
                ? (deepStatus.isRunning ? this.t('analysis.deepScanRunning') : (!deepStatus.enabled ? this.t('analysis.deepScanDisabled') : this.t('analysis.deepScanIdle')))
                : '';
            html += `<div class="an-panel" style="margin-bottom:16px">
                <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:${deepStatus ? '12px' : '4px'};flex-wrap:wrap;gap:8px">
                    <h3 class="an-panel-title" style="margin:0;color:${C_deep}">${this.t('analysis.deepScanProgress')}</h3>
                    <div style="display:flex;gap:8px;align-items:center">
                        ${statusLabel ? `<span id="ds-status" style="font-size:12px;color:var(--text-secondary)">${statusLabel}</span>` : ''}
                        <button class="an-export-btn" onclick="App._deepScanTrigger()" style="border-color:${C_deep};color:${C_deep}">${this.t('analysis.deepScanTrigger')}</button>
                        <button class="an-export-btn" onclick="App._deepScanReset()" style="border-color:var(--text-muted);color:var(--text-muted);font-size:11px">${this.t('analysis.deepScanReset')}</button>
                    </div>
                </div>`;
            if (deepStatus) {
                html += `<div style="display:flex;gap:24px;flex-wrap:wrap;margin-bottom:12px">
                    <span><span id="ds-analyzed" style="color:${C_deep};font-size:22px;font-weight:700">${deepStatus.analyzed.toLocaleString()}</span> <span style="font-size:12px;color:var(--text-secondary)">${this.t('analysis.deepScanAnalyzed')}</span></span>
                    <span><span id="ds-pending" style="font-size:22px;font-weight:700;color:var(--text-secondary)">${deepStatus.pending.toLocaleString()}</span> <span style="font-size:12px;color:var(--text-secondary)">${this.t('analysis.deepScanPending')}</span></span>
                    <span><span id="ds-total" style="font-size:22px;font-weight:700;color:var(--text-secondary)">${deepStatus.total.toLocaleString()}</span> <span style="font-size:12px;color:var(--text-secondary)">${this.t('analysis.deepScanTotal')}</span></span>
                </div>
                <div style="background:var(--bg-hover);border-radius:4px;height:8px;overflow:hidden">
                    <div id="ds-bar" style="width:${pct}%;height:100%;background:${C_deep};transition:width .5s ease;border-radius:4px"></div>
                </div>
                <div style="font-size:11px;color:var(--text-muted);margin-top:4px;text-align:right"><span id="ds-pct">${pct}%</span></div>`;
            } else {
                html += `<p style="color:var(--text-muted);font-size:12px;margin:0">${this.t('analysis.deepScanUnavailable')}</p>`;
            }
            html += `</div>`;

            // Charts — only when data is available
            if (deepStatus && deepStatus.analyzed > 0) {
                const deep = await this.api('analysis/deep');
                if (deep && deep.analyzed > 0) {
                    const deepTotal = deep.analyzed;
                    html += '<div class="an-panels-row">';
                    html += `<div class="an-panel"><h3 class="an-panel-title">${this.t('analysis.exactResolutions')}</h3>${barChart(deep.exactResolutions, deepTotal, C_deep)}</div>`;
                    html += `<div class="an-panel"><h3 class="an-panel-title">${this.t('analysis.hdrCoverage')}</h3>${barChart(deep.hdrCoverage, deepTotal, '#f39c12')}</div>`;
                    html += '</div>';
                    html += '<div class="an-panels-row">';
                    html += `<div class="an-panel"><h3 class="an-panel-title">${this.t('analysis.audioCodecs')}</h3>${barChart(deep.audioCodecs, null, C_deep)}</div>`;
                    html += `<div class="an-panel"><h3 class="an-panel-title">${this.t('analysis.subtitleCodecs')}</h3>${barChart(deep.subtitleCodecs, null, '#9b59b6')}</div>`;
                    html += '</div>';
                    html += '<div class="an-panels-row">';
                    html += `<div class="an-panel"><h3 class="an-panel-title">${this.t('analysis.audioLanguages')}</h3>${barChart(deep.audioLanguages, null, C_deep)}</div>`;
                    html += `<div class="an-panel"><h3 class="an-panel-title">${this.t('analysis.subtitleLanguages')}</h3>${barChart(deep.subtitleLanguages, null, '#9b59b6')}</div>`;
                    html += '</div>';
                    const hasBitrates = deep.bitrateDistribution && deep.bitrateDistribution.length > 0;
                    const hasProfiles = deep.videoProfiles && deep.videoProfiles.length > 0;
                    if (hasBitrates || hasProfiles) {
                        html += '<div class="an-panels-row">';
                        if (hasBitrates) html += `<div class="an-panel"><h3 class="an-panel-title">${this.t('analysis.bitrateDistribution')}</h3>${barChart(deep.bitrateDistribution, deepTotal, '#e67e22')}</div>`;
                        if (hasProfiles) html += `<div class="an-panel"><h3 class="an-panel-title">${this.t('analysis.videoProfiles')}</h3>${barChart(deep.videoProfiles, deepTotal, C_deep)}</div>`;
                        else html += '<div class="an-panel"></div>';
                        html += '</div>';
                    }
                }
            }
        }

        el.innerHTML = html;
    },

    async _deepScanTrigger() {
        const btn = document.querySelector('[onclick="App._deepScanTrigger()"]');
        if (btn) { btn.disabled = true; btn.textContent = 'Starting…'; }
        await this.apiPost('analysis/deep/trigger', {});
        // Poll status every 2s and update progress numbers live until scan completes
        this._deepScanPollStart();
    },

    _deepScanPollStart() {
        if (this._deepScanPollTimer) return; // already polling
        const C_deep = '#00bcd4';
        this._deepScanPollTimer = setInterval(async () => {
            const s = await this.api('analysis/deep/status');
            if (!s) return;
            // Update analyzed / pending / total numbers and bar
            const analyzed = document.getElementById('ds-analyzed');
            const pending  = document.getElementById('ds-pending');
            const total    = document.getElementById('ds-total');
            const bar      = document.getElementById('ds-bar');
            const pctEl    = document.getElementById('ds-pct');
            const statusEl = document.getElementById('ds-status');
            const btn      = document.querySelector('[onclick="App._deepScanTrigger()"]');
            if (!analyzed) { this._deepScanPollStop(); return; } // page navigated away
            const pct = s.total > 0 ? Math.round(s.analyzed / s.total * 100) : 0;
            analyzed.textContent = s.analyzed.toLocaleString();
            pending.textContent  = s.pending.toLocaleString();
            total.textContent    = s.total.toLocaleString();
            if (bar)    bar.style.width = pct + '%';
            if (pctEl)  pctEl.textContent = pct + '%';
            if (statusEl) statusEl.textContent = s.isRunning ? 'Running…' : (s.pending === 0 ? 'Complete' : 'Idle');
            if (btn) { btn.disabled = s.isRunning; btn.textContent = s.isRunning ? 'Running…' : 'Scan Now'; }
            if (!s.isRunning) this._deepScanPollStop();
        }, 2000);
    },

    _deepScanPollStop() {
        clearInterval(this._deepScanPollTimer);
        this._deepScanPollTimer = null;
    },

    async _deepScanReset() {
        if (!confirm(this.t('analysis.deepScanResetConfirm'))) return;
        await this.apiPost('analysis/deep/reset', {});
        if (this.currentPage === 'analysis') await this.navigate('analysis');
    },

    // ─── Music Library Page (with sub-navigation tabs) ──
    async renderMusic(el) {
        this.musicPage = 1;
        this.musicSort = 'title';
        this.musicSubView = 'all';
        this.musicFormat = '';
        this._musicFormats = null;

        let html = '<div class="music-sub-nav">';
        html += `<button class="music-sub-tab active" onclick="App.switchMusicView('all', this)">${this.t('page.musicLibrary')}</button>`;
        html += `<button class="music-sub-tab" onclick="App.switchMusicView('albums', this)">${this.t('page.albums')}</button>`;
        html += `<button class="music-sub-tab" onclick="App.switchMusicView('artists', this)">${this.t('page.artists')}</button>`;
        html += `<button class="music-sub-tab" onclick="App.switchMusicView('genres', this)">${this.t('page.genres')}</button>`;
        html += '</div>';
        html += '<div id="music-sub-content"><div class="spinner"></div></div>';
        el.innerHTML = html;

        await this.loadMusicPage();
    },

    async switchMusicView(view, btn) {
        this.musicSubView = view;
        document.querySelectorAll('.music-sub-tab').forEach(t => t.classList.remove('active'));
        if (btn) btn.classList.add('active');

        const container = document.getElementById('music-sub-content');
        if (!container) return;
        container.innerHTML = '<div class="spinner"></div>';

        switch (view) {
            case 'albums':
                await this.renderAlbums(container);
                break;
            case 'artists':
                await this.renderArtists(container);
                break;
            case 'genres':
                await this.renderGenres(container);
                break;
            default:
                this.musicPage = 1;
                this.musicSort = 'title';
                await this.loadMusicPage();
                break;
        }
    },

    async loadMusicPage(el) {
        const target = el || document.getElementById('music-sub-content') || document.getElementById('main-content');
        const fmtParam = this.musicFormat ? `&format=${this.musicFormat}` : '';
        const [data, allFormats] = await Promise.all([
            this.api(`tracks?limit=${this.musicPerPage}&page=${this.musicPage}&sort=${this.musicSort}${fmtParam}`),
            this._musicFormats ? Promise.resolve(null) : this.api('tracks/formats')
        ]);
        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load music library.'); return; }

        // Use full-library format list from endpoint; fall back to accumulating from page data
        if (allFormats && Array.isArray(allFormats) && allFormats.length > 0) {
            this._musicFormats = new Set(allFormats);
        } else if (!this._musicFormats) {
            this._musicFormats = new Set();
            (data.tracks || []).forEach(t => { const f = this.trackFormat(t); if (f) this._musicFormats.add(f); });
        }
        const fmtList = [...this._musicFormats].sort();

        this.musicTotal = data.total;
        const totalPages = Math.ceil(data.total / this.musicPerPage);
        const sortLabels = { title: this.t('sort.az'), recent: this.t('sort.recent') };

        let html = `<div class="page-header" style="margin-top:4px">
            <div style="font-size:13px;color:var(--text-secondary)">${data.total} tracks in library</div>
            <div class="filter-bar">
            <button class="mv-shuffle-btn" onclick="App.shuffleMusic()" title="Play a random track">
                <svg><use href="#icon-shuffle"/></svg> ${this.t('btn.shufflePlay')}
            </button>
            <button class="mv-nightclub-btn" onclick="App.startNightClubModeFromMusic()" title="Night Club Mode">
                <svg viewBox="0 0 24 16"><path d="M0 8 C2 2, 4 2, 6 8 S10 14, 12 8 S16 2, 18 8 S22 14, 24 8"/></svg> Night Club Mode
            </button>`;
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.musicSort === key ? ' active' : ''}" onclick="App.changeMusicSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

        // Format filter bar — own row, always visible when formats are known
        if (fmtList.length > 0) {
            html += `<div class="songs-format-bar">`;
            html += `<span class="songs-format-label">Format:</span>`;
            html += `<button class="filter-chip${!this.musicFormat ? ' active' : ''}" onclick="App.changeMusicFormat('')">All</button>`;
            fmtList.forEach(f => {
                const cls = this.trackFormatClass(f);
                html += `<button class="filter-chip${this.musicFormat === f.toLowerCase() ? ' active' : ''}" onclick="App.changeMusicFormat('${f.toLowerCase()}')"><span class="track-format-badge ${cls}" style="margin-left:0">${f}</span></button>`;
            });
            html += `</div>`;
        }

        if (data.tracks && data.tracks.length > 0) {
            if (totalPages > 1) {
                html += `<div style="text-align:right;margin-bottom:12px;color:var(--text-muted);font-size:12px">Page ${this.musicPage} of ${totalPages}</div>`;
            }
            html += '<div class="songs-grid">';
            data.tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                const favClass = t.isFavourite ? 'active' : '';
                const fmt = this.trackFormat(t);
                html += `<div class="song-card" onclick="App.playMusicFromCards(${i})" data-track-id="${t.id}">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`
                        }
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playMusicFromCards(${i})">&#9654;</button>
                        <button class="song-card-dots" onclick="event.stopPropagation(); App.showTrackMenu(${t.id}, event)" title="More options">&#8942;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta">
                            <span>${this.esc(t.album)}</span>
                            <span>${dur}</span>
                            ${fmt ? `<span class="track-format-badge ${this.trackFormatClass(fmt)}">${fmt}</span>` : ''}
                        </div>
                    </div>
                    <button class="song-card-fav ${favClass}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
            html += this.renderPagination(this.musicPage, totalPages);
        } else {
            html += this.emptyState(this.t('empty.noMusic.title'), this.t('empty.noMusic.desc'));
        }
        target.innerHTML = html;
    },

    changeMusicSort(sort) {
        this.musicSort = sort;
        this.musicPage = 1;
        this.loadMusicPage();
    },

    changeMusicFormat(fmt) {
        this.musicFormat = fmt;
        this.musicPage = 1;
        this.loadMusicPage();
    },

    playHomeTrack(index) {
        const tracks = this._homeRecentTracks || [];
        if (!tracks[index]) return;
        this.playlist = [...tracks];
        this.playIndex = index;
        this.playTrack(tracks[index]);
    },

    async shuffleMusic() {
        const track = await this.api('tracks/random');
        if (track && track.id) {
            this.shuffle = true;
            document.getElementById('btn-shuffle').style.color = 'var(--accent)';
            this.playlist = [track];
            this.playIndex = 0;
            this.playTrack(track);
        }
    },

    async startNightClubModeFromMusic() {
        // Start shuffling the full library, then launch Night Club Mode overlay.
        // Clear _playlistTracks so startNightClubMode won't re-queue a playlist on top.
        this._playlistTracks = [];
        await this.shuffleMusic();
        this.startNightClubMode();
    },

    goMusicPage(page) {
        const totalPages = Math.ceil(this.musicTotal / this.musicPerPage);
        if (page < 1 || page > totalPages) return;
        this.musicPage = page;
        this.loadMusicPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    getArtUrl(track) {
        if (track.albumArtCached) return `/albumart/${track.albumArtCached}`;
        if (track.hasAlbumArt) return `/api/cover/track/${track.id}`;
        return '';
    },

    playMusicFromCards(index) {
        const cards = document.querySelectorAll('.songs-grid .song-card');
        if (cards.length > 0) {
            this.playlist = Array.from(cards).map(card => ({
                id: parseInt(card.dataset.trackId),
                title: card.querySelector('.song-card-title')?.textContent || '',
                artist: card.querySelector('.song-card-artist')?.textContent || '',
                album: card.querySelector('.song-card-meta span')?.textContent || '',
                hasAlbumArt: true
            }));
        }
        this.playIndex = index;
        if (this.playlist[index]) this.playTrack(this.playlist[index]);
    },

    renderPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goMusicPage(${currentPage - 1})">&laquo; ${this.t('pagination.prev')}</button>`;

        // Show page numbers with ellipsis
        const pages = [];
        pages.push(1);
        let rangeStart = Math.max(2, currentPage - 2);
        let rangeEnd = Math.min(totalPages - 1, currentPage + 2);
        if (rangeStart > 2) pages.push('...');
        for (let p = rangeStart; p <= rangeEnd; p++) pages.push(p);
        if (rangeEnd < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);

        pages.forEach(p => {
            if (p === '...') {
                html += '<span class="page-ellipsis">...</span>';
            } else {
                html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goMusicPage(${p})">${p}</button>`;
            }
        });

        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goMusicPage(${currentPage + 1})">${this.t('pagination.next')} &raquo;</button>`;
        html += '</div>';
        return html;
    },

    // ─── Albums Page ─────────────────────────────────────────
    _albumsPage: 1,
    _albumsTotal: 0,
    _albumsSort: 'recent',

    async renderAlbums(el) {
        this._albumsPage = 1;
        this._albumsSort = 'recent';
        await this.loadAlbumsPage(el);
    },

    async loadAlbumsPage(el) {
        const perPage = 100;
        const target = el || document.getElementById('music-sub-content');
        const data = await this.api(`albums?limit=${perPage}&page=${this._albumsPage}&sort=${this._albumsSort}`);
        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load albums.'); return; }
        this._albumsTotal = data.total;
        const totalPages = Math.ceil(data.total / perPage);

        let html = `<div class="page-header"><h1>${this.t('page.albums')}</h1>
            <div class="filter-bar">
                <button class="filter-chip${this._albumsSort === 'recent' ? ' active' : ''}" onclick="App.changeAlbumsSort('recent')">${this.t('sort.recent')}</button>
                <button class="filter-chip${this._albumsSort === 'name' ? ' active' : ''}" onclick="App.changeAlbumsSort('name')">${this.t('sort.az')}</button>
                <button class="filter-chip${this._albumsSort === 'artist' ? ' active' : ''}" onclick="App.changeAlbumsSort('artist')">${this.t('sort.artist')}</button>
                <button class="filter-chip${this._albumsSort === 'year' ? ' active' : ''}" onclick="App.changeAlbumsSort('year')">${this.t('sort.year')}</button>
            </div>
        </div>`;

        if (data.albums && data.albums.length > 0) {
            if (totalPages > 1) {
                html += `<div style="text-align:right;margin-bottom:12px;color:var(--text-muted);font-size:12px">Page ${this._albumsPage} of ${totalPages} &middot; ${data.total} albums</div>`;
            }
            html += '<div class="card-grid">';
            data.albums.forEach(album => {
                html += this.albumCard(album);
            });
            html += '</div>';
            if (totalPages > 1) html += this.renderAlbumsPagination(this._albumsPage, totalPages);
        } else {
            html += this.emptyState(this.t('empty.noAlbums.title'), this.t('empty.noAlbums.desc'));
        }
        target.innerHTML = html;
    },

    changeAlbumsSort(sort) {
        this._albumsSort = sort;
        this._albumsPage = 1;
        this.loadAlbumsPage();
    },

    renderAlbumsPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goAlbumsPage(${currentPage - 1})">&laquo; ${this.t('pagination.prev')}</button>`;
        const pages = [];
        pages.push(1);
        let rs = Math.max(2, currentPage - 2), re = Math.min(totalPages - 1, currentPage + 2);
        if (rs > 2) pages.push('...');
        for (let p = rs; p <= re; p++) pages.push(p);
        if (re < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goAlbumsPage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goAlbumsPage(${currentPage + 1})">${this.t('pagination.next')} &raquo;</button></div>`;
        return html;
    },

    goAlbumsPage(page) {
        const totalPages = Math.ceil(this._albumsTotal / 100);
        if (page < 1 || page > totalPages) return;
        this._albumsPage = page;
        this.loadAlbumsPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    albumCard(album) {
        return `<div class="card" onclick="App.openAlbum(${album.id})">
            <div class="card-cover">
                <img src="/api/cover/${album.id}" onerror="this.style.display='none';this.parentElement.innerHTML='<div class=placeholder-icon>&#128191;</div>'" alt="">
                <button class="card-play-btn" onclick="event.stopPropagation(); App.playAlbum(${album.id})">&#9654;</button>
            </div>
            <div class="card-info">
                <div class="card-title">${this.esc(album.name)}</div>
                <div class="card-subtitle">${this.esc(album.artist)}${album.year ? ' &middot; ' + album.year : ''}</div>
            </div>
            <button class="mv-card-menu-btn" onclick="event.stopPropagation(); App.showAlbumMenu(${album.id}, event)" title="More options">&#8942;</button>
        </div>`;
    },

    async openAlbum(id) {
        const album = await this.api(`albums/${id}`);
        if (!album) return;
        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(album.name)}</span>`;
        let html = `<div class="page-header">
            <div><h1>${this.esc(album.name)}</h1>
                <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">
                    ${this.esc(album.artist)}${album.year ? ' &middot; ' + album.year : ''} &middot;
                    ${album.tracks ? album.tracks.length : 0} tracks &middot; ${this.formatDuration(album.totalDuration)}
                </div>
            </div>
            <button class="btn-primary" onclick="App.playAlbum(${album.id})">&#9654; ${this.t('btn.playAll')}</button>
        </div>`;
        if (album.tracks && album.tracks.length > 0) html += this.renderTrackTable(album.tracks, true);
        el.innerHTML = html;

        // Auto-play the album from the first track
        if (album.tracks && album.tracks.length > 0) {
            this.playlist = album.tracks;
            this.playIndex = 0;
            this.playTrack(this.playlist[0]);
        }
    },

    async playAlbum(id) {
        const album = await this.api(`albums/${id}`);
        if (!album || !album.tracks) return;
        this.playlist = album.tracks;
        this.playIndex = 0;
        this.playTrack(this.playlist[0]);
    },

    // ─── Artists Page ────────────────────────────────────────
    _artistsPage: 1,
    _artistsTotal: 0,

    async renderArtists(el) {
        this._artistsPage = 1;
        await this.loadArtistsPage(el);
    },

    async loadArtistsPage(el) {
        const perPage = 100;
        const target = el || document.getElementById('music-sub-content');
        const data = await this.api(`artists?limit=${perPage}&page=${this._artistsPage}`);
        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load artists.'); return; }
        this._artistsTotal = data.total;
        const totalPages = Math.ceil(data.total / perPage);

        let html = `<div class="page-header"><h1>${this.t('page.artists')}</h1>
            <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap">
                <div style="font-size:13px;color:var(--text-secondary)">${data.total} artists</div>
                <button class="filter-chip" id="btn-fetch-artist-images" onclick="App.fetchArtistImages()">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;vertical-align:middle;margin-right:4px"><use href="#icon-download"/></svg>Fetch Artist Images</button>
            </div></div>`;
        if (data.artists && data.artists.length > 0) {
            if (totalPages > 1) {
                html += `<div style="text-align:right;margin-bottom:12px;color:var(--text-muted);font-size:12px">Page ${this._artistsPage} of ${totalPages}</div>`;
            }
            html += '<div class="card-grid">';
            data.artists.forEach(artist => {
                const imgHtml = artist.imagePath
                    ? `<img src="/singerphoto/${artist.imagePath}" class="artist-portrait" loading="lazy"
                           onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                       <div class="artist-portrait-fallback" style="display:none">
                           <svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-music"/></svg></div>`
                    : `<div class="artist-portrait-fallback">
                           <svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-music"/></svg></div>`;
                html += `<div class="card" onclick="App.openArtist('${this.esc(artist.name).replace(/'/g, "\\'")}')">
                    <div class="card-cover artist-cover">${imgHtml}</div>
                    <div class="card-info">
                        <div class="card-title">${this.esc(artist.name)}</div>
                        <div class="card-subtitle">${artist.albumCount || 0} albums &middot; ${artist.trackCount || 0} tracks</div>
                    </div>
                </div>`;
            });
            html += '</div>';
            if (totalPages > 1) html += this.renderArtistsPagination(this._artistsPage, totalPages);
        } else {
            html += this.emptyState(this.t('empty.noArtists.title'), this.t('empty.noArtists.desc'));
        }
        target.innerHTML = html;
    },

    renderArtistsPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goArtistsPage(${currentPage - 1})">&laquo; ${this.t('pagination.prev')}</button>`;
        const pages = [];
        pages.push(1);
        let rs = Math.max(2, currentPage - 2), re = Math.min(totalPages - 1, currentPage + 2);
        if (rs > 2) pages.push('...');
        for (let p = rs; p <= re; p++) pages.push(p);
        if (re < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goArtistsPage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goArtistsPage(${currentPage + 1})">${this.t('pagination.next')} &raquo;</button></div>`;
        return html;
    },

    goArtistsPage(page) {
        const perPage = 100;
        const totalPages = Math.ceil(this._artistsTotal / perPage);
        if (page < 1 || page > totalPages) return;
        this._artistsPage = page;
        this.loadArtistsPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    async fetchArtistImages() {
        const btn = document.getElementById('btn-fetch-artist-images');
        if (btn) { btn.disabled = true; btn.innerHTML = 'Fetching portraits…'; }
        try {
            const res = await fetch('/api/artists/fetch-images', { method: 'POST' });
            if (!res.ok) {
                if (btn) { btn.disabled = false; btn.innerHTML = 'Fetch Artist Images'; }
                return;
            }
        } catch {
            if (btn) { btn.disabled = false; btn.innerHTML = 'Fetch Artist Images'; }
            return;
        }
        // Poll until complete, then reload the artists page
        if (this._artistFetchPoll) clearInterval(this._artistFetchPoll);
        this._artistFetchPoll = setInterval(async () => {
            const status = await this.api('artists/fetch-images/status').catch(() => null);
            if (!status?.inProgress) {
                clearInterval(this._artistFetchPoll);
                this._artistFetchPoll = null;
                this.loadArtistsPage();
            }
        }, 2000);
    },

    _artistName: '',
    _artistPage: 1,
    _artistTracks: [],

    async openArtist(name) {
        this._artistName = name;
        this._artistPage = 1;
        await this.loadArtistPage();
    },

    async loadArtistPage() {
        const name = this._artistName;
        const perPage = 100;
        const data = await this.api(`tracks?artist=${encodeURIComponent(name)}&limit=${perPage}&page=${this._artistPage}`);
        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(name)}</span>`;
        const total = data ? data.total : 0;
        const totalPages = Math.ceil(total / perPage);

        let html = `<div class="page-header"><h1>${this.esc(name)}</h1>
            <div style="font-size:13px;color:var(--text-secondary)">${total} tracks</div></div>`;

        if (data && data.tracks && data.tracks.length > 0) {
            this._artistTracks = data.tracks;
            if (totalPages > 1) {
                html += `<div style="text-align:right;margin-bottom:12px;color:var(--text-muted);font-size:12px">Page ${this._artistPage} of ${totalPages}</div>`;
            }
            html += '<div class="songs-grid">';
            data.tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                const favClass = t.isFavourite ? 'active' : '';
                const fmt = this.trackFormat(t);
                html += `<div class="song-card" onclick="App.playArtistTrack(${i})" data-track-id="${t.id}">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`
                        }
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playArtistTrack(${i})">&#9654;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta">
                            <span>${this.esc(t.album)}</span>
                            <span>${dur}</span>
                            ${fmt ? `<span class="track-format-badge ${this.trackFormatClass(fmt)}">${fmt}</span>` : ''}
                        </div>
                    </div>
                    <button class="song-card-fav ${favClass}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
            if (totalPages > 1) html += this.renderArtistPagination(this._artistPage, totalPages);
        } else {
            html += this.emptyState('No tracks found', 'No tracks found for this artist.');
        }
        el.innerHTML = html;
    },

    playArtistTrack(index) {
        this.playlist = [...(this._artistTracks || [])];
        this.playIndex = index;
        if (this.playlist[index]) this.playTrack(this.playlist[index]);
    },

    renderArtistPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goArtistPage(${currentPage - 1})">&laquo; Prev</button>`;
        const pages = [];
        pages.push(1);
        let rs = Math.max(2, currentPage - 2), re = Math.min(totalPages - 1, currentPage + 2);
        if (rs > 2) pages.push('...');
        for (let p = rs; p <= re; p++) pages.push(p);
        if (re < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goArtistPage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goArtistPage(${currentPage + 1})">Next &raquo;</button></div>`;
        return html;
    },

    goArtistPage(page) {
        const perPage = 100;
        const totalPages = Math.ceil((this._artistTracks?.length || 0) / perPage) || 1;
        if (page < 1) return;
        this._artistPage = page;
        this.loadArtistPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    // ─── Songs Page (card layout with big album art) ──────
    songsPage: 1,
    songsSort: 'title',
    songsFormat: '',
    _songsFormats: null,
    songsTotal: 0,
    songsPerPage: 100,

    async renderSongs(el) {
        this.songsPage = 1;
        this.songsSort = 'title';
        this.songsFormat = '';
        this._songsFormats = null; // reset so it rebuilds from page data
        await this.loadSongsPage(el);
    },

    async loadSongsPage(el) {
        const target = el || document.getElementById('main-content');
        const fmtParam = this.songsFormat ? `&format=${this.songsFormat}` : '';
        const data = await this.api(`tracks?limit=${this.songsPerPage}&page=${this.songsPage}&sort=${this.songsSort}${fmtParam}`);
        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load songs.'); return; }

        // Build known-formats set from current page tracks; merge with any previously seen
        if (!this._songsFormats) this._songsFormats = new Set();
        (data.tracks || []).forEach(t => { const f = this.trackFormat(t); if (f) this._songsFormats.add(f); });
        const fmtList = [...this._songsFormats].sort();

        this.songsTotal = data.total;
        const totalPages = Math.ceil(data.total / this.songsPerPage);
        const sortLabels = { title: 'A-Z', artist: 'Artist', album: 'Album', recent: 'Recent' };

        // Page header with sort chips
        let html = `<div class="page-header"><h1>Songs</h1>
            <div class="filter-bar">`;
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.songsSort === key ? ' active' : ''}" onclick="App.changeSongsSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

        // Format filter bar — always its own row beneath the page header
        if (fmtList.length > 0) {
            html += `<div class="songs-format-bar">`;
            html += `<span class="songs-format-label">Format:</span>`;
            html += `<button class="filter-chip${!this.songsFormat ? ' active' : ''}" onclick="App.changeSongsFormat('')">All</button>`;
            fmtList.forEach(f => {
                const cls = this.trackFormatClass(f);
                html += `<button class="filter-chip${this.songsFormat === f.toLowerCase() ? ' active' : ''}" onclick="App.changeSongsFormat('${f.toLowerCase()}')"><span class="track-format-badge ${cls}" style="margin-left:0">${f}</span></button>`;
            });
            html += `</div>`;
        }

        if (data.tracks && data.tracks.length > 0) {
            html += `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;color:var(--text-secondary);font-size:13px">
                <span>${data.total} songs in library</span>
                <span>Page ${this.songsPage} of ${totalPages}</span>
            </div>`;
            html += '<div class="songs-grid">';
            data.tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                const favClass = t.isFavourite ? 'active' : '';
                const fmt = this.trackFormat(t);
                html += `<div class="song-card" onclick="App.playSongFromCards(${i})" data-track-id="${t.id}">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`
                        }
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playSongFromCards(${i})">&#9654;</button>
                        <button class="song-card-dots" onclick="event.stopPropagation(); App.showTrackMenu(${t.id}, event)" title="More options">&#8942;</button>
                        <button class="song-card-add-pl" onclick="App.showAddToPlaylistPopup(${t.id}, this)" title="Add to playlist">+</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta">
                            <span>${this.esc(t.album)}</span>
                            <span>${dur}</span>
                            ${fmt ? `<span class="track-format-badge ${this.trackFormatClass(fmt)}">${fmt}</span>` : ''}
                        </div>
                    </div>
                    <button class="song-card-fav ${favClass}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
            html += this.renderSongsPagination(this.songsPage, totalPages);
        } else {
            html += this.emptyState('No songs found', 'Scan your music library to see all your songs.');
        }
        target.innerHTML = html;
    },

    changeSongsSort(sort) {
        this.songsSort = sort;
        this.songsPage = 1;
        this.loadSongsPage();
    },

    changeSongsFormat(fmt) {
        this.songsFormat = fmt;
        this.songsPage = 1;
        this.loadSongsPage();
    },

    goSongsPage(page) {
        const totalPages = Math.ceil(this.songsTotal / this.songsPerPage);
        if (page < 1 || page > totalPages) return;
        this.songsPage = page;
        this.loadSongsPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    renderSongsPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goSongsPage(${currentPage - 1})">&laquo; Prev</button>`;
        const pages = [];
        pages.push(1);
        let rangeStart = Math.max(2, currentPage - 2);
        let rangeEnd = Math.min(totalPages - 1, currentPage + 2);
        if (rangeStart > 2) pages.push('...');
        for (let p = rangeStart; p <= rangeEnd; p++) pages.push(p);
        if (rangeEnd < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goSongsPage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goSongsPage(${currentPage + 1})">Next &raquo;</button>`;
        html += '</div>';
        return html;
    },

    playSongFromCards(index) {
        const cards = document.querySelectorAll('.songs-grid .song-card');
        if (cards.length > 0) {
            this.playlist = Array.from(cards).map(card => ({
                id: parseInt(card.dataset.trackId),
                title: card.querySelector('.song-card-title')?.textContent || '',
                artist: card.querySelector('.song-card-artist')?.textContent || '',
                album: card.querySelector('.song-card-meta span')?.textContent || '',
                hasAlbumArt: true
            }));
        }
        this.playIndex = index;
        if (this.playlist[index]) this.playTrack(this.playlist[index]);
    },

    // ─── Genres Page ─────────────────────────────────────────
    async renderGenres(el) {
        const [genres, customCats, customGenres] = await Promise.all([
            this.api('genres'),
            this.api('tracks/custom-categories'),
            this.api('custom-genres?domain=music')
        ]);
        let html = `<div class="page-header"><h1>${this.t('page.genres')}</h1></div>`;

        // My Folders section — shown only when the user has library subfolders
        if (customCats && customCats.length > 0) {
            html += `<div class="genre-section-header">
                <span class="genre-section-title">${this.t('filter.myFolders')}</span>
                <button class="cat-manage-btn" onclick="App.openCatManageModal()" title="${this.t('catSettings.manage')}">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2"><use href="#icon-settings"/></svg>
                    ${this.t('catSettings.manage')}
                </button>
            </div>`;
            html += '<div class="genre-grid custom-cat-grid">';
            customCats.forEach(c => {
                const excluded = c.excludedFromLibrary;
                const tooltip = excluded ? ` title="${this.t('catSettings.excludedHint')}"` : '';
                html += `<div class="genre-tag genre-tag-folder${excluded ? ' genre-tag-excluded' : ''}"${tooltip} onclick="App.openCustomCategory('${this.esc(c.name)}')">
                    ${this.esc(c.name)}<span class="genre-count">${c.count}</span>
                    ${excluded ? `<span class="cat-excl-badge">${this.t('catSettings.excluded')}</span>` : ''}
                </div>`;
            });
            html += '</div>';
            if (genres && genres.length > 0) {
                html += `<div class="genre-section-title" style="margin-top:24px">${this.t('page.genres')}</div>`;
            }
        }

        if (genres && genres.length > 0) {
            html += '<div class="genre-grid">';
            genres.forEach(g => {
                html += `<div class="genre-tag" onclick="App.openGenre('${this.esc(g.name)}')">${this.esc(g.name)}<span class="genre-count">${g.count}</span></div>`;
            });
            html += '</div>';
        } else if (!customCats || customCats.length === 0) {
            html += this.emptyState(this.t('empty.noGenres.title'), this.t('empty.noGenres.desc'));
        }

        // Custom Genres section
        const CG_PER_PAGE = 10;
        const cgTotal = customGenres ? customGenres.length : 0;
        const cgTotalPages = Math.ceil(cgTotal / CG_PER_PAGE) || 1;
        const cgPage = Math.min(this._cgMusicPage || 0, cgTotalPages - 1);
        const cgSlice = (customGenres || []).slice(cgPage * CG_PER_PAGE, (cgPage + 1) * CG_PER_PAGE);

        html += `<div class="genre-section-header" style="margin-top:24px">
            <span class="genre-section-title">${this.t('customGenres.title')}</span>
            <div style="display:flex;align-items:center;gap:6px">
                ${cgTotalPages > 1 ? `
                <button class="cg-page-btn" onclick="App._cgMusicNav(-1)" ${cgPage === 0 ? 'disabled' : ''} title="Previous">&#8249;</button>
                <span class="cg-page-info">${cgPage + 1} / ${cgTotalPages}</span>
                <button class="cg-page-btn" onclick="App._cgMusicNav(1)" ${cgPage >= cgTotalPages - 1 ? 'disabled' : ''} title="Next">&#8250;</button>
                ` : ''}
                <button class="cat-manage-btn" onclick="App.openCreateCustomGenreModal('music')">
                    ${this.t('customGenres.add')}
                </button>
            </div>
        </div>`;
        if (cgSlice.length > 0) {
            html += '<div class="genre-grid">';
            cgSlice.forEach(cg => {
                html += `<div class="genre-tag genre-tag-custom" onclick="App.openCustomGenrePage('${this.esc(cg.id)}', 'music')">
                    ${this.esc(cg.name)}<span class="genre-count">${cg.count}</span>
                    <button class="cg-edit-btn" onclick="event.stopPropagation(); App.openEditCustomGenreModal('${this.esc(cg.id)}', 'music')" title="${this.t('customGenres.edit')}"><svg style="width:11px;height:11px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-edit"/></svg></button>
                </div>`;
            });
            html += '</div>';
        } else {
            html += `<p style="color:var(--text-secondary);font-size:13px;margin:6px 0 16px">${this.t('customGenres.empty')}</p>`;
        }

        el.innerHTML = html;
    },

    _genreName: '',
    _genrePage: 1,

    async openGenre(genre) {
        this._genreName = genre;
        this._genrePage = 1;
        await this.loadGenrePage();
    },

    async loadGenrePage() {
        const genre = this._genreName;
        const perPage = 100;
        const data = await this.api(`tracks?genre=${encodeURIComponent(genre)}&limit=${perPage}&page=${this._genrePage}`);
        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>Genre: ${this.esc(genre)}</span>`;
        const total = data ? data.total : 0;
        const totalPages = Math.ceil(total / perPage);

        let html = `<div class="page-header"><h1>${this.esc(genre)}</h1>
            <div style="font-size:13px;color:var(--text-secondary)">${total} tracks</div></div>`;

        if (data && data.tracks && data.tracks.length > 0) {
            this._genreTracks = data.tracks;
            if (totalPages > 1) {
                html += `<div style="text-align:right;margin-bottom:12px;color:var(--text-muted);font-size:12px">Page ${this._genrePage} of ${totalPages}</div>`;
            }
            html += '<div class="songs-grid">';
            data.tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                const favClass = t.isFavourite ? 'active' : '';
                const fmt = this.trackFormat(t);
                html += `<div class="song-card" onclick="App.playGenreTrack(${i})" data-track-id="${t.id}">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`
                        }
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playGenreTrack(${i})">&#9654;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta">
                            <span>${this.esc(t.album)}</span>
                            <span>${dur}</span>
                            ${fmt ? `<span class="track-format-badge ${this.trackFormatClass(fmt)}">${fmt}</span>` : ''}
                        </div>
                    </div>
                    <button class="song-card-fav ${favClass}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
            if (totalPages > 1) html += this.renderGenrePagination(this._genrePage, totalPages);
        } else {
            html += this.emptyState('No tracks found', 'No tracks found for this genre.');
        }
        el.innerHTML = html;
    },

    playGenreTrack(index) {
        this.playlist = [...(this._genreTracks || [])];
        this.playIndex = index;
        if (this.playlist[index]) this.playTrack(this.playlist[index]);
    },

    renderGenrePagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goGenrePage(${currentPage - 1})">&laquo; Prev</button>`;
        const pages = [];
        pages.push(1);
        let rs = Math.max(2, currentPage - 2), re = Math.min(totalPages - 1, currentPage + 2);
        if (rs > 2) pages.push('...');
        for (let p = rs; p <= re; p++) pages.push(p);
        if (re < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goGenrePage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goGenrePage(${currentPage + 1})">Next &raquo;</button></div>`;
        return html;
    },

    goGenrePage(page) {
        const perPage = 100;
        const totalPages = Math.ceil((this._genreTracks?.length || 0) / perPage) || 1;
        if (page < 1) return;
        this._genrePage = page;
        this.loadGenrePage();
        document.getElementById('main-content').scrollTop = 0;
    },

    // ─── Custom Genres ────────────────────────────────────────────
    _cgPageId: '',
    _cgPageDomain: 'music',
    _cgPageName: '',
    _cgPageTracks: [],

    async openCustomGenrePage(id, domain) {
        this._cgPageId = id;
        this._cgPageDomain = domain;
        if (domain === 'music') {
            await this.loadCustomGenrePage();
        } else {
            this.videosCustomGenreId = id;
            this.videosGenre = null;
            this.videosCustomCategory = null;
            this.videosPage = 1;
            this.loadVideosPage();
        }
    },

    async loadCustomGenrePage() {
        const id = this._cgPageId;
        const perPage = 100;
        const genreList = await this.api('custom-genres?domain=music');
        const genre = (genreList || []).find(g => g.id === id);
        const name = genre ? genre.name : id;
        this._cgPageName = name;

        const data = await this.api(`tracks?customGenreId=${encodeURIComponent(id)}&limit=${perPage}&page=1&sort=title`);
        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(name)}</span>`;
        const total = data ? data.total : 0;

        let html = `<div class="page-header"><h1>${this.esc(name)}</h1>
            <div style="font-size:13px;color:var(--text-secondary)">${total} ${this.t('customGenres.items')}</div>
            <button class="btn-secondary" style="margin-left:auto" onclick="App.openEditCustomGenreModal('${this.esc(id)}', 'music')"><svg style="width:13px;height:13px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;vertical-align:middle;margin-right:4px"><use href="#icon-edit"/></svg>${this.t('customGenres.edit')}</button>
        </div>`;

        if (data && data.tracks && data.tracks.length > 0) {
            this._cgPageTracks = data.tracks;
            html += '<div class="songs-grid">';
            data.tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                const favClass = t.isFavourite ? 'active' : '';
                const fmt = this.trackFormat(t);
                html += `<div class="song-card" onclick="App._playCgTrack(${i})" data-track-id="${t.id}">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`
                        }
                        <button class="song-card-play" onclick="event.stopPropagation(); App._playCgTrack(${i})">&#9654;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta">
                            <span>${this.esc(t.album)}</span>
                            <span>${dur}</span>
                            ${fmt ? `<span class="track-format-badge ${this.trackFormatClass(fmt)}">${fmt}</span>` : ''}
                        </div>
                    </div>
                    <button class="song-card-fav ${favClass}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
        } else {
            html += this.emptyState(this.t('customGenres.title'), this.t('customGenres.empty'));
        }
        el.innerHTML = html;
    },

    _playCgTrack(index) {
        this.playlist = [...(this._cgPageTracks || [])];
        this.playIndex = index;
        if (this.playlist[index]) this.playTrack(this.playlist[index]);
    },

    async openCreateCustomGenreModal(domain) {
        this._cgEditId = null;
        this._cgEditDomain = domain;
        await this._showCustomGenreModal(null, domain);
    },

    async openEditCustomGenreModal(id, domain) {
        this._cgEditId = id;
        this._cgEditDomain = domain;
        const genreList = await this.api(`custom-genres?domain=${encodeURIComponent(domain)}`);
        const genre = (genreList || []).find(g => g.id === id);
        await this._showCustomGenreModal(genre || { id, name: '', rules: '[]' }, domain);
    },

    async _showCustomGenreModal(genreData, domain) {
        const isMusic = domain === 'music';
        const domainLabel = isMusic ? this.t('nav.music')
            : domain === 'movie' ? this.t('page.movies')
            : domain === 'tv' ? this.t('page.tvShows')
            : this.t('nav.anime');

        const [availGenres, availFolders] = await Promise.all([
            isMusic ? this.api('genres') : this.api(`videos/genres?mediaType=${encodeURIComponent(domain)}`),
            isMusic ? this.api('tracks/custom-categories') : this.api(`videos/custom-categories?mediaType=${encodeURIComponent(domain)}`)
        ]);

        const currentRules = genreData?.rules
            ? (typeof genreData.rules === 'string' ? JSON.parse(genreData.rules || '[]') : genreData.rules)
            : [];
        const selGenres = new Set(currentRules.filter(r => r.type === 'genre').map(r => r.value));
        const selFolders = new Set(currentRules.filter(r => r.type === 'folder').map(r => r.value));
        const isEdit = !!genreData?.id;
        const title = isEdit ? this.t('customGenres.edit') : this.t('customGenres.create');

        let genreChecks = '';
        (availGenres || []).forEach(g => {
            const checked = selGenres.has(g.name) ? 'checked' : '';
            genreChecks += `<label class="cg-rule-check">
                <input type="checkbox" class="cg-genre-check" value="${this.esc(g.name)}" ${checked}>
                <span>${this.esc(g.name)}</span><span class="genre-count">${g.count}</span>
            </label>`;
        });

        let folderChecks = '';
        (availFolders || []).forEach(f => {
            const checked = selFolders.has(f.name) ? 'checked' : '';
            folderChecks += `<label class="cg-rule-check">
                <input type="checkbox" class="cg-folder-check" value="${this.esc(f.name)}" ${checked}>
                <span>${this.esc(f.name)}</span><span class="genre-count">${f.count}</span>
            </label>`;
        });

        const currentName = genreData?.name || '';
        const deleteBtn = isEdit
            ? `<button class="btn-danger" onclick="App._deleteCustomGenre()">${this.t('customGenres.delete')}</button>`
            : '';

        const overlay = document.createElement('div');
        overlay.id = 'cg-modal-overlay';
        overlay.className = 'cat-modal-overlay';
        overlay.innerHTML = `
            <div class="cat-modal-box cg-modal-box">
                <div class="cat-modal-header">
                    <h3>${title} <span style="font-size:12px;color:var(--text-secondary);font-weight:400">(${domainLabel})</span></h3>
                    <button class="cat-modal-close" onclick="App._closeCgModal()">✕</button>
                </div>
                <div style="margin-bottom:14px">
                    <label style="display:block;font-size:12px;font-weight:600;color:var(--text-secondary);margin-bottom:6px">${this.t('customGenres.name')}</label>
                    <input id="cg-name-input" type="text" class="search-input" style="width:100%;box-sizing:border-box"
                        placeholder="${this.t('customGenres.namePlaceholder')}" value="${this.esc(currentName)}" maxlength="80">
                </div>
                <p style="font-size:12px;color:var(--text-secondary);margin:0 0 12px">${this.t('customGenres.rulesHint')}</p>
                ${availGenres && availGenres.length > 0 ? `
                <div class="cg-rules-section">
                    <div class="cg-rules-label">${this.t('customGenres.fromGenres')}</div>
                    <div class="cg-rules-list">${genreChecks || '<span style="color:var(--text-secondary);font-size:12px">None available</span>'}</div>
                </div>` : ''}
                ${availFolders && availFolders.length > 0 ? `
                <div class="cg-rules-section">
                    <div class="cg-rules-label">${this.t('customGenres.fromFolders')}</div>
                    <div class="cg-rules-list">${folderChecks}</div>
                </div>` : ''}
                <div class="cat-modal-actions" style="justify-content:space-between">
                    <div style="display:flex;gap:8px">
                        <button class="btn-primary" onclick="App._saveCgModal()">${this.t('customGenres.save')}</button>
                        <button class="btn-secondary" onclick="App._closeCgModal()">${this.t('customGenres.cancel')}</button>
                    </div>
                    ${deleteBtn}
                </div>
            </div>`;
        document.body.appendChild(overlay);
        document.getElementById('cg-name-input')?.focus();
    },

    _closeCgModal() {
        document.getElementById('cg-modal-overlay')?.remove();
    },

    async _saveCgModal() {
        const name = document.getElementById('cg-name-input')?.value?.trim();
        if (!name) { alert(this.t('customGenres.noName')); return; }

        const rules = [];
        document.querySelectorAll('.cg-genre-check:checked').forEach(el => {
            rules.push({ type: 'genre', value: el.value });
        });
        document.querySelectorAll('.cg-folder-check:checked').forEach(el => {
            rules.push({ type: 'folder', value: el.value });
        });

        const body = { name, domain: this._cgEditDomain, rules };
        if (this._cgEditId) {
            await this.apiPut(`custom-genres/${encodeURIComponent(this._cgEditId)}`, body);
        } else {
            await this.apiPost('custom-genres', body);
        }

        this._closeCgModal();
        const domain = this._cgEditDomain;
        if (domain === 'music') {
            const el = document.getElementById('main-content');
            if (el) await this.renderGenres(el);
        } else {
            this.videosCustomGenreId = null;
            this.loadVideosPage();
        }
    },

    async _deleteCustomGenre() {
        if (!confirm(this.t('customGenres.deleteConfirm'))) return;
        const id = this._cgEditId;
        const domain = this._cgEditDomain;
        await this.apiDelete(`custom-genres/${encodeURIComponent(id)}?domain=${encodeURIComponent(domain)}`);
        this._closeCgModal();
        if (domain === 'music') {
            const el = document.getElementById('main-content');
            if (el) await this.renderGenres(el);
        } else {
            this.videosCustomGenreId = null;
            this.loadVideosPage();
        }
    },

    // ─── Custom Category (My Folders) ────────────────────────
    _customCatName: '',

    async openCustomCategory(cat) {
        this._customCatName = cat;
        await this.loadCustomCategoryPage();
    },

    async loadCustomCategoryPage() {
        const cat = this._customCatName;
        const perPage = 100;
        const data = await this.api(`tracks?customCategory=${encodeURIComponent(cat)}&limit=${perPage}&page=1&sort=title`);
        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(cat)}</span>`;
        const total = data ? data.total : 0;

        let html = `<div class="page-header"><h1>${this.esc(cat)}</h1>
            <div style="font-size:13px;color:var(--text-secondary)">${total} tracks</div></div>`;

        if (data && data.tracks && data.tracks.length > 0) {
            this._genreTracks = data.tracks; // reuse genre track list for playback
            html += '<div class="songs-grid">';
            data.tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                const favClass = t.isFavourite ? 'active' : '';
                const fmt = this.trackFormat(t);
                html += `<div class="song-card" onclick="App.playGenreTrack(${i})" data-track-id="${t.id}">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`
                        }
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playGenreTrack(${i})">&#9654;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta">
                            <span>${this.esc(t.album)}</span>
                            <span>${dur}</span>
                            ${fmt ? `<span class="track-format-badge ${this.trackFormatClass(fmt)}">${fmt}</span>` : ''}
                        </div>
                    </div>
                    <button class="song-card-fav ${favClass}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
        } else {
            html += this.emptyState('No tracks found', 'No tracks found in this folder category.');
        }
        el.innerHTML = html;
    },

    // ─── Category Settings Modal ─────────────────────────────

    async openCatManageModal() {
        // Fetch current categories (with excludedFromLibrary flag) and current settings
        const [cats, settings] = await Promise.all([
            this.api('tracks/custom-categories'),
            this.api('category-settings')
        ]);
        if (!cats) return;

        const excluded = new Set((settings?.music?.excludedFromLibrary || []).map(s => s.toLowerCase()));

        let rows = '';
        if (cats.length === 0) {
            rows = `<p style="color:var(--text-secondary);font-size:13px">${this.t('catSettings.noFolders')}</p>`;
        } else {
            cats.forEach(c => {
                const isExcluded = excluded.has(c.name.toLowerCase());
                rows += `<div class="cat-manage-row">
                    <div class="cat-manage-info">
                        <span class="cat-manage-name">${this.esc(c.name)}</span>
                        <span class="cat-manage-count">${c.count} ${this.t('catSettings.tracks')}</span>
                    </div>
                    <label class="cat-toggle-label">
                        <input type="checkbox" class="cat-incl-check" data-cat="${this.esc(c.name)}" ${isExcluded ? '' : 'checked'}>
                        <span class="cat-toggle-text">${this.t('catSettings.inMainLibrary')}</span>
                    </label>
                </div>`;
            });
        }

        const modal = document.createElement('div');
        modal.id = 'cat-manage-modal';
        modal.className = 'cat-modal-overlay';
        modal.innerHTML = `
            <div class="cat-modal-box">
                <div class="cat-modal-header">
                    <h3>${this.t('catSettings.title')}</h3>
                    <button class="cat-modal-close" onclick="App.closeCatManageModal()">✕</button>
                </div>
                <p class="cat-modal-hint">${this.t('catSettings.hint')}</p>
                <div class="cat-manage-list">${rows}</div>
                <div class="cat-modal-actions">
                    <button class="btn-primary" onclick="App.saveCatSettings()">${this.t('catSettings.save')}</button>
                    <button class="btn-secondary" onclick="App.closeCatManageModal()">${this.t('catSettings.cancel')}</button>
                </div>
            </div>`;
        document.body.appendChild(modal);
        modal.addEventListener('click', e => { if (e.target === modal) App.closeCatManageModal(); });
    },

    closeCatManageModal() {
        document.getElementById('cat-manage-modal')?.remove();
    },

    async saveCatSettings() {
        const excludedFromLibrary = [];
        document.querySelectorAll('.cat-incl-check').forEach(el => {
            if (!el.checked) excludedFromLibrary.push(el.dataset.cat);
        });
        // Preserve existing hidden list (admin-managed, not touched here)
        const current = await this.api('category-settings');
        const body = {
            music: { excludedFromLibrary, hidden: current?.music?.hidden || [] },
            video: current?.video || { hidden: [] }
        };
        await this.apiPost('category-settings', body);
        this.closeCatManageModal();
        // Refresh genres page to reflect new state
        const el = document.getElementById('main-content');
        if (el) await this.renderGenres(el);
    },

    // ─── Favourites ──────────────────────────────────────────
    async renderFavourites(el) {
        const [trackData, mvData, videoData, radioData, tvData, podcastData, audioBookData] = await Promise.all([
            this.api('tracks/favourites?limit=200'),
            this.api('musicvideos/favourites'),
            this.api('videos/favourites'),
            this.api('radio/favourites'),
            this.api('tvchannels/favourites'),
            this.api('podcasts/favourites'),
            this.api('audiobooks/favourites')
        ]);

        const hasTracks  = trackData && trackData.tracks && trackData.tracks.length > 0;
        const hasMvs     = mvData && mvData.videos && mvData.videos.length > 0;
        const hasVideos  = videoData && videoData.videos && videoData.videos.length > 0;
        const hasRadio   = radioData && radioData.stations && radioData.stations.length > 0;
        const hasTv      = tvData && tvData.channels && tvData.channels.length > 0;
        const hasPodcasts = podcastData && podcastData.podcasts && podcastData.podcasts.length > 0;
        const hasAudioBooks = audioBookData && audioBookData.audioBooks && audioBookData.audioBooks.length > 0;

        let html = `<div class="page-header"><h1>${this.t('page.favourites')}</h1></div>`;

        if (!hasTracks && !hasMvs && !hasVideos && !hasRadio && !hasTv && !hasPodcasts && !hasAudioBooks) {
            html += this.emptyState(this.t('empty.noFavourites.title'), this.t('empty.noFavourites.desc'));
            el.innerHTML = html;
            return;
        }

        // Music tracks
        if (hasTracks) {
            html += `<div class="section-title" style="margin-top:12px">Music (${trackData.tracks.length})</div>`;
            html += '<div class="songs-grid">';
            trackData.tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                const fmt = this.trackFormat(t);
                html += `<div class="song-card" onclick="App.playFavTrack(${i})" data-track-id="${t.id}">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`}
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playFavTrack(${i})">&#9654;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta">
                            <span>${this.esc(t.album)}</span>
                            <span>${dur}</span>
                            ${fmt ? `<span class="track-format-badge ${this.trackFormatClass(fmt)}">${fmt}</span>` : ''}
                        </div>
                    </div>
                    <button class="song-card-fav active" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
            this._favTracks = trackData.tracks;
        }

        // Music Videos
        if (hasMvs) {
            html += `<div class="section-title" style="margin-top:24px">Music Videos (${mvData.videos.length})</div>`;
            html += '<div class="mv-grid">';
            mvData.videos.forEach(v => {
                const thumbSrc = v.thumbnailPath ? `/mvthumb/${v.thumbnailPath}` : '';
                const dur = this.formatDuration(v.duration);
                html += `<div class="mv-card" onclick="App.openMvDetail(${v.id})" data-mv-id="${v.id}">
                    <div class="mv-card-thumb">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="mv-card-placeholder" style="display:none">&#127909;</span>`
                            : `<span class="mv-card-placeholder">&#127909;</span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        <button class="mv-card-play" onclick="event.stopPropagation(); App.playMusicVideo(${v.id})">&#9654;</button>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${this.esc(v.artist)}</div>
                    </div>
                    <button class="song-card-fav active" onclick="event.stopPropagation(); App.toggleMvFav(${v.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
        }

        // Movies & TV
        if (hasVideos) {
            html += `<div class="section-title" style="margin-top:24px">Movies & TV (${videoData.videos.length})</div>`;
            html += '<div class="mv-grid">';
            videoData.videos.forEach(v => {
                const thumbSrc = v.posterPath ? `/videometa/${v.posterPath}` : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
                const dur = this.formatDuration(v.duration);
                const subtitle = v.mediaType === 'tv' && v.seriesName
                    ? `${this.esc(v.seriesName)} S${(v.season||0).toString().padStart(2,'0')}E${(v.episode||0).toString().padStart(2,'0')}`
                    : (v.year ? v.year : '');
                html += `<div class="mv-card" onclick="App.openVideoDetail(${v.id})" data-video-id="${v.id}">
                    <div class="mv-card-thumb">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        <button class="mv-card-play" onclick="event.stopPropagation(); App.playVideo(${v.id})">&#9654;</button>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${subtitle}</div>
                    </div>
                    <button class="song-card-fav active" onclick="event.stopPropagation(); App.toggleVideoFav(${v.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
        }

        // Radio Stations
        if (hasRadio) {
            // Store for local use if needed
            this._favRadioStations = radioData.stations;
            html += `<div class="section-title" style="margin-top:24px">Radio Stations (${radioData.stations.length})</div>`;
            html += '<div class="radio-grid">';
            html += radioData.stations.map(s => {
                const logoUrl = s.logo ? `/radiologo/${this.esc(s.logo)}` : '';
                const logoHtml = logoUrl
                    ? `<img src="${logoUrl}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><div class="radio-card-placeholder" style="display:none"><svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-radio"/></svg></div>`
                    : `<div class="radio-card-placeholder"><svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-radio"/></svg></div>`;
                const playingClass = (this.isRadioPlaying && this.currentRadioStation && this.currentRadioStation.id === s.id) ? ' playing' : '';

                return `<div class="radio-card${playingClass}" data-station-id="${s.id}" onclick="App.playFavRadio(${s.id})">
                    <div class="radio-card-logo">${logoHtml}</div>
                    <div class="radio-card-info">
                        <div class="radio-card-name">${this.esc(s.name)}</div>
                        <div class="radio-card-desc">${this.esc(s.description)}</div>
                        <div class="radio-card-meta">
                            <span class="radio-card-country">${this.esc(s.country)}</span>
                            <span class="radio-card-genre">${this.esc(s.genre)}</span>
                        </div>
                    </div>
                    <button class="radio-card-fav active" onclick="event.stopPropagation(); App.toggleRadioFav(${s.id}, this)">&#10084;</button>
                    <div class="radio-card-play"><svg style="width:18px;height:18px;stroke:#fff;fill:none;stroke-width:2"><use href="#icon-play"/></svg></div>
                </div>`;
            }).join('');
            html += '</div>';
        }

        // TV Channels
        if (hasTv) {
            this._favTvChannels = tvData.channels;
            html += `<div class="section-title" style="margin-top:24px">TV Channels (${tvData.channels.length})</div>`;
            html += '<div class="radio-grid">';
            html += tvData.channels.map(c => {
                const logoUrl = c.logo ? `/tvlogo/${this.esc(c.logo)}` : '';
                const logoHtml = logoUrl
                    ? `<img src="${logoUrl}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><div class="radio-card-placeholder" style="display:none"><svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-tv"/></svg></div>`
                    : `<div class="radio-card-placeholder"><svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-tv"/></svg></div>`;
                return `<div class="radio-card" data-tv-id="${c.id}" onclick="App.playFavTv(${c.id})">
                    <div class="radio-card-logo">${logoHtml}</div>
                    <div class="radio-card-info">
                        <div class="radio-card-name">${this.esc(c.name)}</div>
                        <div class="radio-card-desc">${this.esc(c.description || c.genre || '')}</div>
                        <div class="radio-card-meta">${this.esc(c.country || '')} ${c.genre ? '&middot; ' + this.esc(c.genre) : ''}</div>
                    </div>
                    <button class="radio-card-fav active" onclick="event.stopPropagation(); App.toggleTvFav(${c.id}, this)">&#10084;</button>
                    <div class="radio-card-play"><svg style="width:16px;height:16px;fill:white;stroke:none"><polygon points="5,3 15,10 5,17"/></svg></div>
                </div>`;
            }).join('');
            html += '</div>';
        }

        // Podcasts
        if (hasPodcasts) {
            this._favPodcasts = podcastData.podcasts;
            html += `<div class="section-title" style="margin-top:24px">Podcasts (${podcastData.podcasts.length})</div>`;
            html += '<div class="radio-grid">';
            html += podcastData.podcasts.map(f => {
                const art = f.artworkFile ? `/podcastart/${f.artworkFile}` : '';
                const logoHtml = art
                    ? `<img src="${art}" style="border-radius:8px" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><div class="radio-card-placeholder" style="display:none"><svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-podcast"/></svg></div>`
                    : `<div class="radio-card-placeholder"><svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-podcast"/></svg></div>`;
                const unplayed = f.unplayedCount > 0 ? `<span class="podcast-unplayed-badge">${f.unplayedCount}</span>` : '';
                return `<div class="radio-card" onclick="App.openFavPodcast(${f.id})">
                    <div class="radio-card-logo">${logoHtml}</div>
                    <div class="radio-card-info">
                        <div class="radio-card-name">${this.esc(f.title)} ${unplayed}</div>
                        <div class="radio-card-desc">${this.esc(f.author || '')}</div>
                        <div class="radio-card-meta">
                            <span class="radio-card-country">${this.esc(f.category || '')}</span>
                            ${f.episodeCount ? `<span class="radio-card-genre">${f.episodeCount} episodes</span>` : ''}
                        </div>
                    </div>
                    <button class="radio-card-fav active" onclick="event.stopPropagation(); App.togglePodcastFav(${f.id}, this)">&#10084;</button>
                    <div class="radio-card-play" onclick="event.stopPropagation(); App.openFavPodcast(${f.id})">
                        <svg style="width:16px;height:16px;fill:white;stroke:none"><polygon points="5,3 15,10 5,17"/></svg>
                    </div>
                </div>`;
            }).join('');
            html += '</div>';
        }

        // Audio Books
        if (hasAudioBooks) {
            html += `<div class="section-title" style="margin-top:24px">${this.t('nav.audioBooks')} (${audioBookData.audioBooks.length})</div>`;
            html += '<div class="audiobooks-grid">';
            audioBookData.audioBooks.forEach(book => {
                const fmt = (book.format || 'MP3').toUpperCase();
                const fmtClass = fmt === 'M4B' ? 'm4b' : 'mp3';
                const author = book.author || 'Unknown Author';
                const dur = book.duration > 0 ? this.formatDuration(Math.round(book.duration)) : fmt;
                html += `<div class="ebook-card audiobook-card" onclick="App.openAudioBookDetail(${book.id})" data-audiobook-id="${book.id}">
                    <div class="ebook-card-cover">
                        ${book.coverImage
                            ? `<img src="/audiobookcover/${book.coverImage}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="ebook-card-placeholder" style="display:none">&#127911;</span>`
                            : `<span class="ebook-card-placeholder">&#127911;</span>`}
                        <span class="ebook-format-badge ebook-format-${fmtClass}">${fmt}</span>
                    </div>
                    <div class="ebook-card-info">
                        <div class="ebook-card-title">${this.esc(book.title)}</div>
                        <div class="ebook-card-author">${this.esc(author)}</div>
                        <div class="ebook-card-meta">
                            <span>${dur}</span>
                            <span>${this.formatSize(book.fileSize)}</span>
                        </div>
                    </div>
                    <button class="song-card-fav active" onclick="event.stopPropagation(); App.toggleAudioBookFav(${book.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
        }

        el.innerHTML = html;
    },

    playFavTv(id) {
        const channel = (this._favTvChannels || []).find(c => c.id === id);
        if (channel) {
            this.tvChannels = this._favTvChannels;
            this.playTvChannel(id);
        }
    },

    openFavPodcast(feedId) {
        // Seed podcastFeeds from the favourites list so openPodcast can find it
        if (!this.podcastFeeds || !this.podcastFeeds.find(f => f.id === feedId)) {
            this.podcastFeeds = [...(this._favPodcasts || [])];
        }
        this.navigate('podcasts');
        // Navigate renders the full podcasts page; then open the specific feed
        setTimeout(() => this.openPodcast(feedId), 300);
    },

    playFavRadio(id) {
        const station = (this._favRadioStations || []).find(s => s.id === id);
        if (station) this.playRadioStation(station);
    },

    playFavTrack(index) {
        this.playlist = [...(this._favTracks || [])];
        this.playIndex = index;
        if (this.playlist[index]) this.playTrack(this.playlist[index]);
    },

    openFavAudioBook(id) {
        this.navigate('audiobooks');
        setTimeout(() => this.openAudioBookDetail(id), 300);
    },

    // ─── Watchlist ───────────────────────────────────────────
    async renderWatchlist(el) {
        const data = await this.api('watchlist');
        const videos = data?.videos || [];

        // Populate in-memory set so context menu shows correct state
        this._watchlistIds = new Set(videos.map(v => v.id));

        let html = `<div class="page-header"><h1>${this.t('nav.watchlist')}</h1></div>`;

        if (videos.length === 0) {
            html += this.emptyState(this.t('empty.noWatchlist.title'), this.t('empty.noWatchlist.desc'));
            el.innerHTML = html;
            return;
        }

        html += `<div class="mv-grid">`;
        videos.forEach(v => {
            const thumbSrc = v.posterPath ? `/videometa/${v.posterPath}` : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
            const dur = this.formatDuration(v.duration);
            const subtitle = v.mediaType === 'tv' && v.seriesName
                ? `${this.esc(v.seriesName)} S${(v.season||0).toString().padStart(2,'0')}E${(v.episode||0).toString().padStart(2,'0')}`
                : (v.year ? v.year : '');
            const watchedBadge = v.isWatched ? `<span class="mv-watched-badge"><svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>WATCHED</span>` : '';
            html += `<div class="mv-card${thumbSrc ? ' mv-card-poster' : ''}" onclick="App.openVideoDetail(${v.id})" data-video-id="${v.id}" data-watchlist="1">
                <div class="mv-card-thumb${thumbSrc ? ' mv-poster-thumb' : ''}">
                    ${thumbSrc
                        ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`
                        : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`}
                    <span class="mv-duration-badge">${dur}</span>
                    ${watchedBadge}
                </div>
                <div class="mv-card-info">
                    <div class="mv-card-title">${this.esc(v.title)}</div>
                    <div class="mv-card-artist">${subtitle}</div>
                </div>
                <button class="mv-card-menu-btn" onclick="event.stopPropagation(); App.showVideoMenu(${v.id}, '${v.mediaType}', event)" title="More options">&#8942;</button>
            </div>`;
        });
        html += '</div>';
        el.innerHTML = html;
    },

    async toggleVideoWatchlist(videoId) {
        this.closeVideoMenu();
        try {
            const res = await fetch(`/api/videos/${videoId}/watchlist`, { method: 'POST' });
            if (res.ok) {
                const data = await res.json();
                if (data.inWatchlist) {
                    this._watchlistIds.add(videoId);
                    this.showToast(this.t('watchlist.added'));
                } else {
                    this._watchlistIds.delete(videoId);
                    this.showToast(this.t('watchlist.removed'));
                    // If on the watchlist page, remove the card
                    if (this.currentPage === 'watchlist') {
                        document.querySelector(`[data-video-id="${videoId}"]`)?.remove();
                    }
                }
            }
        } catch (e) { console.error('Failed to toggle watchlist:', e); }
    },

    // ─── Playlists ───────────────────────────────────────────
    async renderPlaylists(el) {
        const [playlists, agpConfig] = await Promise.all([
            this.api('playlists'),
            this.api('agp-config')
        ]);
        this._playlistsData = playlists || [];

        let html = `<div class="page-header"><h1>${this.t('page.playlists')}</h1>
            <div style="display:flex;gap:8px;align-items:center">
                <button class="btn-import-pl" onclick="App.importPlaylist()">&#8679; ${this.t('btn.importPlaylist')}</button>
                <button class="btn-primary" style="margin-top:0" onclick="App.createPlaylist()">+ ${this.t('btn.createPlaylist')}</button>
            </div>
        </div>`;

        // Manual playlists
        if (playlists && playlists.length > 0) {
            html += '<div class="card-grid">';
            playlists.forEach(p => {
                const coverHtml = p.coverImagePath
                    ? `<img src="/albumart/${this.esc(p.coverImagePath)}" style="width:100%;height:100%;object-fit:cover" alt="">`
                    : `<div class="placeholder-icon"><svg width="40" height="40" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" style="opacity:.35"><use href="#icon-music"/></svg></div>`;
                html += `<div class="card" onclick="App.openPlaylist(${p.id})">
                    <div class="card-cover">${coverHtml}</div>
                    <div class="card-info">
                        <div class="card-title">${this.esc(p.name)}</div>
                        <div class="card-subtitle">${p.trackCount} tracks</div>
                    </div>
                    <button class="mv-card-menu-btn" onclick="event.stopPropagation(); App.showPlaylistMenu(${p.id}, event)" title="More options">&#8942;</button>
                </div>`;
            });
            html += '</div>';
        } else {
            html += this.emptyState(this.t('empty.noPlaylists.title'), this.t('empty.noPlaylists.desc'));
        }

        // Auto-generated playlists section
        html += `<div class="gp-section-header">
            <span><svg width="16" height="16" style="vertical-align:-2px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-layers"/></svg> ${this.t('autoplaylists.sectionTitle')}</span>
            <button class="agp-btn agp-btn-primary" onclick="App._agpTogglePanel()" id="agp-toggle-btn">
                <svg width="14" height="14" style="vertical-align:-1px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-plus"/></svg> ${this.t('autoplaylists.generate')}
            </button>
        </div>
        <div id="agp-panel" style="display:none">
            <div class="agp-panel">
                <p class="agp-panel-hint">${this.t('autoplaylists.selectHint')}</p>
                <div class="agp-criteria-group-label">${this.t('autoplaylists.groupHistory')}</div>
                <div class="agp-criteria-grid">
                    <div class="agp-criteria-card" id="agp-card-decade" onclick="App._agpToggle('decade')">
                        <div class="agp-criteria-icon"><svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-clock"/></svg></div>
                        <div class="agp-criteria-name">${this.t('autoplaylists.decade')}</div>
                        <div class="agp-check"><svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1.5,5 4,7.5 8.5,2"/></svg></div>
                    </div>
                    <div class="agp-criteria-card" id="agp-card-topplayed" onclick="App._agpToggle('topplayed')">
                        <div class="agp-criteria-icon"><svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-trending"/></svg></div>
                        <div class="agp-criteria-name">${this.t('autoplaylists.topPlayed')}</div>
                        <div class="agp-check"><svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1.5,5 4,7.5 8.5,2"/></svg></div>
                    </div>
                    <div class="agp-criteria-card" id="agp-card-recent" onclick="App._agpToggle('recent')">
                        <div class="agp-criteria-icon"><svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-refresh"/></svg></div>
                        <div class="agp-criteria-name">${this.t('autoplaylists.recent')}</div>
                        <div class="agp-check"><svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1.5,5 4,7.5 8.5,2"/></svg></div>
                    </div>
                    <div class="agp-criteria-card" id="agp-card-favourites" onclick="App._agpToggle('favourites')">
                        <div class="agp-criteria-icon"><svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-heart"/></svg></div>
                        <div class="agp-criteria-name">${this.t('autoplaylists.favourites')}</div>
                        <div class="agp-check"><svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1.5,5 4,7.5 8.5,2"/></svg></div>
                    </div>
                </div>
                <div class="agp-criteria-group-label" style="margin-top:10px">${this.t('autoplaylists.groupGenre')}</div>
                <div class="agp-criteria-grid">
                    <div class="agp-criteria-card" id="agp-card-genre_rock" onclick="App._agpToggle('genre_rock')">
                        <div class="agp-criteria-icon"><svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-music"/></svg></div>
                        <div class="agp-criteria-name">${this.t('autoplaylists.genreRock')}</div>
                        <div class="agp-check"><svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1.5,5 4,7.5 8.5,2"/></svg></div>
                    </div>
                    <div class="agp-criteria-card" id="agp-card-genre_rap" onclick="App._agpToggle('genre_rap')">
                        <div class="agp-criteria-icon"><svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-music"/></svg></div>
                        <div class="agp-criteria-name">${this.t('autoplaylists.genreRap')}</div>
                        <div class="agp-check"><svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1.5,5 4,7.5 8.5,2"/></svg></div>
                    </div>
                    <div class="agp-criteria-card" id="agp-card-genre_country" onclick="App._agpToggle('genre_country')">
                        <div class="agp-criteria-icon"><svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-music"/></svg></div>
                        <div class="agp-criteria-name">${this.t('autoplaylists.genreCountry')}</div>
                        <div class="agp-check"><svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1.5,5 4,7.5 8.5,2"/></svg></div>
                    </div>
                    <div class="agp-criteria-card" id="agp-card-genre_rnb" onclick="App._agpToggle('genre_rnb')">
                        <div class="agp-criteria-icon"><svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-music"/></svg></div>
                        <div class="agp-criteria-name">${this.t('autoplaylists.genreRnb')}</div>
                        <div class="agp-check"><svg width="10" height="10" viewBox="0 0 10 10" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1.5,5 4,7.5 8.5,2"/></svg></div>
                    </div>
                </div>
                <div class="agp-panel-actions">
                    <button class="agp-btn agp-btn-cancel" onclick="App._agpTogglePanel()">${this.t('btn.cancel')}</button>
                    <button class="agp-btn agp-btn-primary" onclick="App._agpGenerate()">${this.t('autoplaylists.generate')}</button>
                </div>
            </div>
        </div>
        <div id="agp-sections"></div>`;

        el.innerHTML = html;
        const agpActiveTypes = agpConfig?.activeTypes || agpConfig?.ActiveTypes;
        if (agpActiveTypes?.length > 0) {
            document.getElementById('agp-sections').innerHTML =
                '<div style="padding:20px;text-align:center;color:var(--text-secondary);font-size:13px">Loading…</div>';
            await this._agpLoadFromConfig(agpConfig);
        }
    },

    async createPlaylist() {
        const name = prompt('Enter playlist name:');
        if (!name) return;
        await this.apiPost('playlists', { name, description: '' });
        this.renderPage('playlists');
    },

    // ── Playlist import (M3U / M3U8 / PLS) ───────────────────────
    importPlaylist() {
        let inp = document.getElementById('_import-pl-file-input');
        if (!inp) {
            inp = document.createElement('input');
            inp.type = 'file';
            inp.id = '_import-pl-file-input';
            inp.accept = '.m3u,.m3u8,.pls';
            inp.style.display = 'none';
            inp.addEventListener('change', e => {
                const file = e.target.files[0];
                inp.value = '';
                if (!file) return;
                const reader = new FileReader();
                reader.onload = ev => {
                    const ext = file.name.split('.').pop().toLowerCase();
                    const entries = ext === 'pls'
                        ? this._parsePls(ev.target.result)
                        : this._parseM3u(ev.target.result);
                    const defaultName = file.name.replace(/\.(m3u8?|pls)$/i, '');
                    this._showImportModal(entries, defaultName, ext.toUpperCase());
                };
                reader.readAsText(file, 'utf-8');
            });
            document.body.appendChild(inp);
        }
        inp.click();
    },

    _parseM3u(text) {
        const lines = text.split(/\r?\n/);
        const entries = [];
        let pending = {};
        for (const line of lines) {
            const t = line.trim();
            if (!t || t === '#EXTM3U') continue;
            if (t.startsWith('#EXTINF:')) {
                const comma = t.indexOf(',');
                if (comma !== -1) {
                    const info = t.slice(comma + 1).trim();
                    const dash = info.indexOf(' - ');
                    pending = dash !== -1
                        ? { artist: info.slice(0, dash).trim(), title: info.slice(dash + 3).trim() }
                        : { title: info, artist: null };
                }
            } else if (!t.startsWith('#')) {
                entries.push({ path: t, title: pending.title || null, artist: pending.artist || null });
                pending = {};
            }
        }
        return entries;
    },

    _parsePls(text) {
        const lines = text.split(/\r?\n/);
        const files = {}, titles = {};
        for (const line of lines) {
            const t = line.trim();
            const fm = t.match(/^File(\d+)=(.+)$/i);
            if (fm) { files[fm[1]] = fm[2].trim(); continue; }
            const tm = t.match(/^Title(\d+)=(.+)$/i);
            if (tm) { titles[tm[1]] = tm[2].trim(); }
        }
        return Object.keys(files).sort((a, b) => +a - +b).map(k => {
            const rawTitle = titles[k] || null;
            let artist = null, title = rawTitle;
            if (rawTitle) {
                const dash = rawTitle.indexOf(' - ');
                if (dash !== -1) { artist = rawTitle.slice(0, dash).trim(); title = rawTitle.slice(dash + 3).trim(); }
            }
            return { path: files[k], title, artist };
        });
    },

    _showImportModal(entries, defaultName, format) {
        this._pendingImportEntries = entries;
        document.getElementById('_import-pl-overlay')?.remove();
        const overlay = document.createElement('div');
        overlay.id = '_import-pl-overlay';
        overlay.className = 'import-pl-overlay';
        overlay.addEventListener('click', e => { if (e.target === overlay) overlay.remove(); });
        overlay.innerHTML = `
            <div class="import-pl-modal">
                <div class="import-pl-header">
                    <h3>${this.t('playlist.import.title')}</h3>
                </div>
                <div class="import-pl-body">
                    <label class="import-pl-label">${this.t('playlist.import.nameLabel')}</label>
                    <input type="text" id="_import-pl-name" class="import-pl-input"
                           value="${this.esc(defaultName)}" placeholder="${this.t('playlist.import.namePlaceholder')}">
                    <div class="import-pl-meta">
                        <span>${entries.length} ${this.t('playlist.import.tracksFound')}</span>
                        <span class="import-pl-format-badge">${format}</span>
                    </div>
                    <div class="import-pl-hint">${this.t('playlist.import.hint')}</div>
                    <div id="_import-pl-result" class="import-pl-result" style="display:none"></div>
                </div>
                <div class="import-pl-footer">
                    <button class="btn-secondary" onclick="document.getElementById('_import-pl-overlay').remove()">
                        ${this.t('btn.cancel')}
                    </button>
                    <button class="btn-primary" id="_import-pl-btn" onclick="App._doImportPlaylist()">
                        ${this.t('playlist.import.doImport')}
                    </button>
                </div>
            </div>`;
        document.body.appendChild(overlay);
        document.getElementById('_import-pl-name')?.focus();
    },

    async _doImportPlaylist() {
        const entries = this._pendingImportEntries || [];
        const nameEl  = document.getElementById('_import-pl-name');
        const name    = nameEl?.value.trim() || 'Imported Playlist';
        const btn     = document.getElementById('_import-pl-btn');
        const result  = document.getElementById('_import-pl-result');

        if (btn) { btn.disabled = true; btn.textContent = this.t('playlist.import.importing'); }

        const res = await this.apiPost('playlists/import-entries', { name, entries }).catch(() => null);

        if (!res) {
            if (btn) { btn.disabled = false; btn.textContent = this.t('playlist.import.doImport'); }
            if (result) { result.style.display = 'block'; result.className = 'import-pl-result import-pl-result--error';
                          result.textContent = this.t('playlist.import.errorGeneric'); }
            return;
        }

        if (res.matched === 0) {
            if (btn) { btn.disabled = false; btn.textContent = this.t('playlist.import.doImport'); }
            if (result) { result.style.display = 'block'; result.className = 'import-pl-result import-pl-result--warn';
                          result.textContent = this.t('playlist.import.noneMatched'); }
            return;
        }

        document.getElementById('_import-pl-overlay')?.remove();
        this._pendingImportEntries = null;
        this.renderPage('playlists');
    },

    async openPlaylist(id) {
        const data = await this.api(`playlists/${id}`);
        if (!data) return;
        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(data.name)}</span>`;

        // Store playlist tracks for playback
        const entries = (data.playlistTracks || []).filter(pt => pt.track);
        this._playlistTracks = entries.map(pt => pt.track);
        this._playlistId = id;

        // Calculate total duration
        const totalSecs = this._playlistTracks.reduce((s, t) => s + (t.duration || 0), 0);

        let html = `<div class="playlist-header">
            <div class="playlist-header-info">
                <div class="playlist-header-name" id="pl-name-display">
                    ${this.esc(data.name)}
                </div>
                <div class="playlist-header-meta">${entries.length} tracks &middot; ${this.formatDuration(totalSecs)}</div>
                ${data.description ? `<div class="playlist-header-desc">${this.esc(data.description)}</div>` : ''}
            </div>
        </div>`;

        // Action buttons
        html += `<div class="playlist-actions">
            <button class="playlist-btn playlist-btn-play" onclick="App.playPlaylistAll()"${entries.length === 0 ? ' disabled' : ''}>&#9654; ${this.t('btn.playAll')}</button>
            <button class="playlist-btn playlist-btn-shuffle" onclick="App.playPlaylistShuffle()"${entries.length === 0 ? ' disabled' : ''}>
                <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-shuffle"/></svg> Shuffle
            </button>
            <button class="playlist-btn playlist-btn-nightclub" onclick="App.startNightClubMode()"${entries.length === 0 ? ' disabled' : ''}>
                <svg style="width:16px;height:14px" viewBox="0 0 24 16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M0 8 C2 2, 4 2, 6 8 S10 14, 12 8 S16 2, 18 8 S22 14, 24 8"/></svg> Night Club Mode
            </button>
            <button class="playlist-btn playlist-btn-delete" onclick="App.deletePlaylist(${id})">Delete Playlist</button>
        </div>`;

        // Track list
        if (entries.length > 0) {
            html += '<div class="playlist-track-list">';
            entries.forEach((pt, i) => {
                const t = pt.track;
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                const favClass = t.isFavourite ? 'active' : '';
                html += `<div class="playlist-track-row" onclick="App.playPlaylistFromIndex(${i})" data-track-id="${t.id}">
                    <span class="playlist-track-num">${i + 1}</span>
                    <div class="playlist-track-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='block'" alt=""><span class="playlist-track-art-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="playlist-track-art-placeholder">&#9835;</span>`}
                    </div>
                    <div class="playlist-track-info">
                        <div class="playlist-track-title">${this.esc(t.title)}</div>
                        <div class="playlist-track-sub">${this.esc(t.artist)} &middot; ${this.esc(t.album)}</div>
                    </div>
                    <span class="playlist-track-dur">${dur}</span>
                    <div class="playlist-track-actions">
                        <button class="playlist-track-fav ${favClass}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                        <button class="playlist-track-remove" onclick="event.stopPropagation(); App.removePlaylistTrack(${id}, ${pt.id}, this)" title="Remove from playlist">&#10005;</button>
                    </div>
                </div>`;
            });
            html += '</div>';
        } else {
            html += this.emptyState('Empty playlist', 'Add tracks from the Songs page using the + button on song cards.');
        }
        el.innerHTML = html;
    },

    playPlaylistAll() {
        if (!this._playlistTracks || this._playlistTracks.length === 0) return;
        this.playlist = [...this._playlistTracks];
        this.playIndex = 0;
        this.playTrack(this.playlist[0]);
    },

    playPlaylistShuffle() {
        if (!this._playlistTracks || this._playlistTracks.length === 0) return;
        const shuffled = [...this._playlistTracks];
        for (let i = shuffled.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [shuffled[i], shuffled[j]] = [shuffled[j], shuffled[i]];
        }
        this.playlist = shuffled;
        this.playIndex = 0;
        this.playTrack(this.playlist[0]);
    },

    playPlaylistFromIndex(index) {
        if (!this._playlistTracks || !this._playlistTracks[index]) return;
        this.playlist = [...this._playlistTracks];
        this.playIndex = index;
        this.playTrack(this.playlist[index]);
    },

    async removePlaylistTrack(playlistId, entryId, btn) {
        const row = btn.closest('.playlist-track-row');
        const res = await this.apiDelete(`playlists/${playlistId}/tracks/${entryId}`);
        if (res && res.message) {
            if (row) { row.style.transition = 'opacity .3s, transform .3s'; row.style.opacity = '0'; row.style.transform = 'translateX(30px)'; setTimeout(() => row.remove(), 300); }
            // Update stored tracks
            if (this._playlistTracks) {
                const trackId = parseInt(row?.dataset?.trackId);
                this._playlistTracks = this._playlistTracks.filter(t => t.id !== trackId);
            }
        }
    },

    async editPlaylist(id, currentCover, currentName) {
        // currentName may come from caller; fall back to DOM if inside playlist detail view
        if (!currentName) {
            const nameEl = document.getElementById('pl-name-display');
            currentName = nameEl ? nameEl.childNodes[0].textContent.trim() : '';
        }
        this._plEditCover = currentCover || '';

        const overlay = document.createElement('div');
        overlay.id = 'plEditOverlay';
        overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.65);z-index:3000;display:flex;align-items:center;justify-content:center';
        overlay.innerHTML = `
        <div style="background:var(--bg-surface);border-radius:12px;padding:28px;width:460px;max-width:95vw;box-shadow:0 8px 40px rgba(0,0,0,.5);position:relative">
            <button onclick="document.getElementById('plEditOverlay').remove()" style="position:absolute;top:12px;right:14px;background:none;border:none;color:var(--text-secondary);font-size:20px;cursor:pointer;line-height:1">&times;</button>
            <h2 style="margin:0 0 20px;font-size:17px;display:flex;align-items:center;gap:8px">
                <svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/></svg>
                Edit Playlist
            </h2>
            <label style="display:block;font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;color:var(--text-secondary);margin-bottom:6px">Name</label>
            <input id="plEditName" type="text" value="${this.esc(currentName)}"
                style="width:100%;box-sizing:border-box;background:var(--bg-input,var(--bg-hover));border:1px solid var(--border);border-radius:7px;padding:9px 12px;color:var(--text-primary);font-size:14px;margin-bottom:18px">
            <label style="display:block;font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;color:var(--text-secondary);margin-bottom:8px">Cover Image</label>
            <div style="display:flex;align-items:center;gap:12px;margin-bottom:20px">
                <div id="plEditCoverPreview" style="width:72px;height:72px;border-radius:8px;overflow:hidden;background:var(--bg-hover);border:1px solid var(--border);flex-shrink:0;display:flex;align-items:center;justify-content:center">
                    ${this._plEditCover
                        ? `<img src="/albumart/${this.esc(this._plEditCover)}" style="width:100%;height:100%;object-fit:cover" alt="">`
                        : `<svg width="32" height="32" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" style="opacity:.4"><use href="#icon-music"/></svg>`}
                </div>
                <div style="display:flex;flex-direction:column;gap:7px">
                    <button onclick="App._plPickCover()" style="background:var(--bg-hover);border:1px solid var(--border);border-radius:7px;padding:7px 14px;color:var(--text-primary);font-size:13px;cursor:pointer;display:flex;align-items:center;gap:7px">
                        <svg width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                        Pick from Album Art…</button>
                    ${this._plEditCover ? `<button onclick="App._plClearCover()" style="background:none;border:none;color:var(--text-secondary);font-size:12px;cursor:pointer;text-align:left;padding:0;display:flex;align-items:center;gap:5px"><svg width="12" height="12" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg> Remove cover</button>` : ''}
                </div>
            </div>
            <div style="display:flex;justify-content:flex-end;gap:10px">
                <button onclick="document.getElementById('plEditOverlay').remove()" style="background:var(--bg-hover);border:1px solid var(--border);border-radius:7px;padding:9px 18px;color:var(--text-primary);font-size:13px;cursor:pointer">Cancel</button>
                <button onclick="App._plEditSave(${id})" style="background:var(--accent);border:none;border-radius:7px;padding:9px 18px;color:#fff;font-size:13px;font-weight:600;cursor:pointer">Save Changes</button>
            </div>
        </div>`;
        document.body.appendChild(overlay);
        overlay.addEventListener('mousedown', (e) => { overlay._mdBackdrop = (e.target === overlay); });
        overlay.addEventListener('click', (e) => { if (e.target === overlay && overlay._mdBackdrop) overlay.remove(); });
        document.getElementById('plEditName').focus();
        document.getElementById('plEditName').select();
    },

    _plClearCover() {
        this._plEditCover = '';
        const prev = document.getElementById('plEditCoverPreview');
        if (prev) prev.innerHTML = `<svg width="32" height="32" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" style="opacity:.4"><use href="#icon-music"/></svg>`;
        // hide remove button since no cover
        const removeBtn = prev?.nextElementSibling?.querySelector('button:last-child');
        if (removeBtn && removeBtn.textContent.includes('Remove')) removeBtn.style.display = 'none';
    },

    async _plPickCover() {
        return this._openMusicArtPicker('playlist');
    },


    _plSelectCover(filename) {
        this._plEditCover = filename;
        const prev = document.getElementById('plEditCoverPreview');
        if (prev) prev.innerHTML = `<img src="/albumart/${this.esc(filename)}" style="width:100%;height:100%;object-fit:cover" alt="">`;
        const picker = document.getElementById('plCoverPickerOverlay');
        if (picker) picker.remove();
    },

    async _plEditSave(id) {
        const nameEl = document.getElementById('plEditName');
        const newName = nameEl ? nameEl.value.trim() : '';
        if (!newName) return;
        const res = await this.apiPut(`playlists/${id}`, { name: newName, description: null, coverImagePath: this._plEditCover });
        const overlay = document.getElementById('plEditOverlay');
        if (overlay) overlay.remove();
        if (res) this.renderPage('playlists');
    },

    async deletePlaylist(id) {
        if (!confirm('Delete this playlist? This cannot be undone.')) return;
        const res = await this.apiDelete(`playlists/${id}`);
        if (res && res.message) this.renderPage('playlists');
    },

    // ─── Auto-Generated Playlists ─────────────────────────────────────

    _agpTogglePanel() {
        const panel = document.getElementById('agp-panel');
        if (!panel) return;
        const open = panel.style.display === 'none';
        panel.style.display = open ? 'block' : 'none';
        const btn = document.getElementById('agp-toggle-btn');
        if (btn) btn.classList.toggle('active', open);

        // When opening, sync card selections to reflect what's already active
        if (open) {
            const activeTypes = this._agpConfig?.activeTypes || this._agpConfig?.ActiveTypes || [];
            const allTypes = ['decade', 'topplayed', 'recent', 'favourites', 'genre_rock', 'genre_rap', 'genre_country', 'genre_rnb'];
            // If no config yet, leave all selected (first-time setup)
            if (activeTypes.length > 0) {
                allTypes.forEach(t => {
                    const card = document.getElementById(`agp-card-${t}`);
                    if (card) card.classList.toggle('agp-selected', activeTypes.includes(t));
                });
            }
        }
    },

    _agpToggle(type) {
        const card = document.getElementById(`agp-card-${type}`);
        if (!card) return;
        card.classList.toggle('agp-selected');
    },

    async _agpGenerate() {
        const allTypes = ['decade', 'topplayed', 'recent', 'favourites', 'genre_rock', 'genre_rap', 'genre_country', 'genre_rnb'];
        const types = allTypes.filter(t =>
            document.getElementById(`agp-card-${t}`)?.classList.contains('agp-selected')
        );
        if (!types.length) return;

        const btn = document.querySelector('#agp-panel .agp-btn-primary');
        if (btn) { btn.disabled = true; btn.textContent = this.t('player.loading'); }

        const config = {
            activeTypes: types,
            excludedDecades: this._agpConfig?.excludedDecades || this._agpConfig?.ExcludedDecades || [],
            excludedTracks: this._agpConfig?.excludedTracks || this._agpConfig?.ExcludedTracks || {},
            covers: this._agpConfig?.covers || this._agpConfig?.Covers || {}
        };
        await this.apiPost('agp-config', config);
        this._agpTogglePanel();

        document.getElementById('agp-sections').innerHTML =
            '<div style="padding:20px;text-align:center;color:var(--text-secondary);font-size:13px">Loading…</div>';
        await this._agpLoadFromConfig(config);
    },

    async _agpLoadFromConfig(config) {
        const types = config.activeTypes || config.ActiveTypes || [];
        this._agpExcludedDecades = new Set((config.excludedDecades || config.ExcludedDecades || []).map(Number));
        this._agpConfig = config;

        const _agpMergeGenre = (...terms) => Promise.all(
            terms.map(t => this.api(`tracks?genreSearch=${encodeURIComponent(t)}&limit=2000&sort=artist`))
        ).then(results => {
            const seen = new Set(), merged = [];
            for (const r of results) for (const t of (r?.tracks || []))
                if (!seen.has(t.id)) { seen.add(t.id); merged.push(t); }
            return { tracks: merged };
        });

        const fetches = {};
        if (types.includes('decade'))        fetches.decade        = this.api('decades');
        if (types.includes('topplayed'))     fetches.topplayed     = this.api('tracks/mostplayed?limit=100')
                                                 .then(r => ({ tracks: Array.isArray(r) ? r : [] }));
        if (types.includes('recent'))        fetches.recent        = this.api('tracks?sort=recent&limit=100');
        if (types.includes('favourites'))    fetches.favourites    = this.api('tracks?favouritesOnly=true&limit=2000&sort=title');
        if (types.includes('genre_rock'))    fetches.genre_rock    = _agpMergeGenre('rock');
        if (types.includes('genre_rap'))     fetches.genre_rap     = _agpMergeGenre('rap', 'hip-hop');
        if (types.includes('genre_country')) fetches.genre_country = _agpMergeGenre('country');
        if (types.includes('genre_rnb'))     fetches.genre_rnb     = _agpMergeGenre('r&b', 'soul');

        const keys = Object.keys(fetches);
        const results = await Promise.all(keys.map(k => fetches[k]));
        this._agpSections = {};
        keys.forEach((k, i) => { this._agpSections[k] = results[i]; });
        this._agpRenderSections();
    },

    _agpRenderSections() {
        const container = document.getElementById('agp-sections');
        if (!container || !this._agpSections) return;

        const iconSvg = (id, size = 28) =>
            `<svg width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-${id}"/></svg>`;

        let html = '';
        const covers = this._agpConfig?.covers || this._agpConfig?.Covers || {};

        const agpCoverHtml = (key, fallbackContent) => {
            const c = covers[key];
            return c
                ? `<img src="/albumart/${this.esc(c)}" style="width:100%;height:100%;object-fit:cover" alt="">`
                : fallbackContent;
        };

        const agpMenuBtn = (type, param) => {
            const paramStr = param !== undefined ? `, ${param}` : '';
            return `<button class="mv-card-menu-btn" onclick="event.stopPropagation(); App.showAgpMenu('${type}'${paramStr}, event)" title="More options">&#8942;</button>`;
        };

        // Decade
        if (this._agpSections.decade) {
            const excluded = this._agpExcludedDecades || new Set();
            const decades = this._agpSections.decade.filter(d => !excluded.has(d.decade));
            html += `<div class="gp-section-header"><span>${iconSvg('clock', 16)} ${this.t('autoplaylists.decade')}</span></div>`;
            if (decades.length) {
                html += '<div class="card-grid">';
                decades.forEach(d => {
                    const label = d.decade + 's';
                    const coverKey = `decade_${d.decade}`;
                    html += `<div class="card" onclick="App.openAgpPlaylist('decade',${d.decade})">
                        <div class="card-cover">${agpCoverHtml(coverKey, `<div class="agp-decade-cover">${this.esc(label)}</div>`)}</div>
                        <div class="card-info">
                            <div class="card-title">${this.esc(label)}</div>
                            <div class="card-subtitle">${d.count} ${this.t('autoplaylists.tracks')}</div>
                        </div>
                        ${agpMenuBtn('decade', d.decade)}
                    </div>`;
                });
                html += '</div>';
            } else {
                html += `<p style="color:var(--text-secondary);font-size:13px;padding:8px 0">${this.t('autoplaylists.noDecades')}</p>`;
            }
        }

        // Top Played
        if (this._agpSections.topplayed) {
            const tracks = this._agpSections.topplayed.tracks || [];
            html += `<div class="gp-section-header"><span>${iconSvg('trending', 16)} ${this.t('autoplaylists.topPlayed')}</span></div>`;
            html += `<div class="card-grid">
                <div class="card" onclick="App.openAgpPlaylist('topplayed')">
                    <div class="card-cover">${agpCoverHtml('topplayed', `<div class="placeholder-icon">${iconSvg('trending')}</div>`)}</div>
                    <div class="card-info">
                        <div class="card-title">${this.t('autoplaylists.top100')}</div>
                        <div class="card-subtitle">${tracks.length} ${this.t('autoplaylists.tracks')}</div>
                    </div>
                    ${agpMenuBtn('topplayed')}
                </div>
            </div>`;
        }

        // Recently Added
        if (this._agpSections.recent) {
            const tracks = this._agpSections.recent.tracks || [];
            html += `<div class="gp-section-header"><span>${iconSvg('refresh', 16)} ${this.t('autoplaylists.recent')}</span></div>`;
            html += `<div class="card-grid">
                <div class="card" onclick="App.openAgpPlaylist('recent')">
                    <div class="card-cover">${agpCoverHtml('recent', `<div class="placeholder-icon">${iconSvg('refresh')}</div>`)}</div>
                    <div class="card-info">
                        <div class="card-title">${this.t('autoplaylists.last90days')}</div>
                        <div class="card-subtitle">${tracks.length} ${this.t('autoplaylists.tracks')}</div>
                    </div>
                    ${agpMenuBtn('recent')}
                </div>
            </div>`;
        }

        // Favourites
        if (this._agpSections.favourites) {
            const tracks = this._agpSections.favourites.tracks || [];
            html += `<div class="gp-section-header"><span>${iconSvg('heart', 16)} ${this.t('autoplaylists.favourites')}</span></div>`;
            html += `<div class="card-grid">
                <div class="card" onclick="App.openAgpPlaylist('favourites')">
                    <div class="card-cover">${agpCoverHtml('favourites', `<div class="placeholder-icon">${iconSvg('heart')}</div>`)}</div>
                    <div class="card-info">
                        <div class="card-title">${this.t('autoplaylists.allFavourites')}</div>
                        <div class="card-subtitle">${tracks.length} ${this.t('autoplaylists.tracks')}</div>
                    </div>
                    ${agpMenuBtn('favourites')}
                </div>
            </div>`;
        }

        // Genre playlists
        const genreDefs = [
            { key: 'genre_rock',    labelKey: 'autoplaylists.genreRock' },
            { key: 'genre_rap',     labelKey: 'autoplaylists.genreRap' },
            { key: 'genre_country', labelKey: 'autoplaylists.genreCountry' },
            { key: 'genre_rnb',     labelKey: 'autoplaylists.genreRnb' },
        ];
        const genreCards = genreDefs.filter(g => this._agpSections[g.key]);
        if (genreCards.length) {
            html += `<div class="gp-section-header"><span>${iconSvg('music', 16)} ${this.t('autoplaylists.groupGenre')}</span></div>`;
            html += '<div class="card-grid">';
            genreCards.forEach(g => {
                const tracks = this._agpSections[g.key].tracks || [];
                if (!tracks.length) return;
                const label = this.t(g.labelKey);
                html += `<div class="card" onclick="App.openAgpPlaylist('${g.key}')">
                    <div class="card-cover">${agpCoverHtml(g.key, `<div class="placeholder-icon">${iconSvg('music')}</div>`)}</div>
                    <div class="card-info">
                        <div class="card-title">${label}</div>
                        <div class="card-subtitle">${tracks.length} ${this.t('autoplaylists.tracks')}</div>
                    </div>
                    ${agpMenuBtn(g.key)}
                </div>`;
            });
            html += '</div>';
        }

        container.innerHTML = html;
    },

    async openAgpPlaylist(type, param) {
        const el = document.getElementById('main-content');
        let title = '', fetchUrl = '', sectionData = this._agpSections?.[type];

        const genreLabelMap = { genre_rock: 'autoplaylists.genreRock', genre_rap: 'autoplaylists.genreRap', genre_country: 'autoplaylists.genreCountry', genre_rnb: 'autoplaylists.genreRnb' };
        if (type === 'decade') {
            const label = param + 's';
            title = label;
            fetchUrl = `tracks?yearFrom=${param}&yearTo=${param + 9}&limit=2000&sort=artist`;
        } else if (type === 'topplayed') {
            title = this.t('autoplaylists.top100');
        } else if (type === 'recent') {
            title = this.t('autoplaylists.last90days');
        } else if (type === 'favourites') {
            title = this.t('autoplaylists.allFavourites');
        } else if (genreLabelMap[type]) {
            title = this.t(genreLabelMap[type]);
        }

        document.getElementById('page-title').innerHTML = `<span>${this.esc(title)}</span>`;
        el.innerHTML = '<div style="padding:40px;text-align:center;color:var(--text-secondary)">Loading…</div>';

        // Ensure config is fresh (may not have been loaded via _agpLoadFromConfig)
        if (!this._agpConfig) this._agpConfig = await this.api('agp-config') || {};

        let tracks;
        if (fetchUrl) {
            const data = await this.api(fetchUrl);
            tracks = data?.tracks || [];
        } else {
            tracks = sectionData?.tracks || [];
        }

        // Filter out excluded tracks for this playlist
        const _agpKey = type === 'decade' ? 'decade_' + param : type;
        const _agpExclSet = new Set(((this._agpConfig?.excludedTracks || this._agpConfig?.ExcludedTracks || {})[_agpKey] || []).map(Number));
        if (_agpExclSet.size) tracks = tracks.filter(t => !_agpExclSet.has(t.id));
        this._agpCurrentType = type;
        this._agpCurrentParam = param;
        this._agpCurrentKey = _agpKey;

        this._agpCurrentTracks = tracks;
        this._playlistTracks = tracks;
        const totalSecs = tracks.reduce((s, t) => s + (t.duration || 0), 0);
        const dis = tracks.length === 0 ? ' disabled' : '';

        let html = `<div class="playlist-header">
            <div class="playlist-header-info">
                <div class="playlist-header-name">${this.esc(title)}</div>
                <div class="playlist-header-meta">${tracks.length} ${this.t('autoplaylists.tracks')} &middot; ${this.formatDuration(totalSecs)}</div>
            </div>
        </div>
        <div class="playlist-actions">
            <button class="playlist-btn playlist-btn-play" onclick="App._agpPlayAll()"${dis}>
                <svg style="width:13px;height:13px;fill:currentColor"><use href="#icon-play"/></svg> ${this.t('btn.playAll')}
            </button>
            <button class="playlist-btn playlist-btn-shuffle" onclick="App._agpShuffle()"${dis}>
                <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-shuffle"/></svg> Shuffle
            </button>
            <button class="playlist-btn playlist-btn-nightclub" onclick="App.startNightClubMode()"${dis}>
                <svg style="width:16px;height:14px" viewBox="0 0 24 16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M0 8 C2 2, 4 2, 6 8 S10 14, 12 8 S16 2, 18 8 S22 14, 24 8"/></svg> Night Club Mode
            </button>
            <button class="playlist-btn playlist-btn-delete" onclick="App._agpDeletePlaylist('${type}',${JSON.stringify(param ?? null)})">Delete Playlist</button>
        </div>`;

        if (tracks.length > 0) {
            html += '<div class="playlist-track-list">';
            tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                html += `<div class="playlist-track-row" onclick="App._agpPlayFromIndex(${i})">
                    <span class="playlist-track-num">${i + 1}</span>
                    <div class="playlist-track-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='block'" alt=""><span class="playlist-track-art-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="playlist-track-art-placeholder">&#9835;</span>`}
                    </div>
                    <div class="playlist-track-info">
                        <div class="playlist-track-title">${this.esc(t.title)}</div>
                        <div class="playlist-track-sub">${this.esc(t.artist)} &middot; ${this.esc(t.album)}</div>
                    </div>
                    <span class="playlist-track-dur">${dur}</span>
                    <div class="playlist-track-actions">
                        <button class="playlist-track-fav ${t.isFavourite ? 'active' : ''}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                        <button class="playlist-track-remove" onclick="event.stopPropagation(); App._agpRemoveTrack('${type}',${JSON.stringify(param ?? null)},${t.id},this)" title="Remove from playlist">&#10005;</button>
                    </div>
                </div>`;
            });
            html += '</div>';
        } else {
            html += this.emptyState(this.t('autoplaylists.noTracks'), '');
        }
        el.innerHTML = html;
    },

    _agpPlayAll() {
        if (!this._agpCurrentTracks?.length) return;
        this.playlist = [...this._agpCurrentTracks];
        this.playIndex = 0;
        this.playTrack(this.playlist[0]);
    },

    _agpShuffle() {
        if (!this._agpCurrentTracks?.length) return;
        const shuffled = [...this._agpCurrentTracks];
        for (let i = shuffled.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [shuffled[i], shuffled[j]] = [shuffled[j], shuffled[i]];
        }
        this.playlist = shuffled;
        this.playIndex = 0;
        this.playTrack(this.playlist[0]);
    },

    _agpPlayFromIndex(idx) {
        if (!this._agpCurrentTracks?.[idx]) return;
        this.playlist = [...this._agpCurrentTracks];
        this.playIndex = idx;
        this.playTrack(this.playlist[idx]);
    },

    async _agpDeletePlaylist(type, param) {
        const raw = await this.api('agp-config') || {};
        // Normalise — guard against any PascalCase keys from old stored data
        const activeTypes = raw.activeTypes || raw.ActiveTypes || [];
        const excludedDecades = raw.excludedDecades || raw.ExcludedDecades || [];
        const config = { activeTypes: [...activeTypes], excludedDecades: [...excludedDecades] };
        if (type === 'decade' && param != null) {
            config.excludedDecades = [...new Set([...config.excludedDecades, Number(param)])];
        } else {
            config.activeTypes = config.activeTypes.filter(t => t !== type);
        }
        await this.apiPost('agp-config', config);
        this.renderPage('playlists');
    },

    async _agpRemoveTrack(type, param, trackId, btn) {
        const row = btn.closest('.playlist-track-row');
        const raw = await this.api('agp-config') || {};
        const config = {
            activeTypes: raw.activeTypes || raw.ActiveTypes || [],
            excludedDecades: raw.excludedDecades || raw.ExcludedDecades || [],
            excludedTracks: { ...(raw.excludedTracks || raw.ExcludedTracks || {}) }
        };
        const key = type === 'decade' ? 'decade_' + param : type;
        const existing = new Set((config.excludedTracks[key] || []).map(Number));
        existing.add(Number(trackId));
        config.excludedTracks[key] = [...existing];
        await this.apiPost('agp-config', config);
        // Update in-memory config so re-opening the playlist stays filtered
        if (!this._agpConfig) this._agpConfig = {};
        if (!this._agpConfig.excludedTracks) this._agpConfig.excludedTracks = {};
        this._agpConfig.excludedTracks[key] = [...existing];
        // Animate row out
        if (row) {
            row.style.transition = 'opacity .3s, transform .3s';
            row.style.opacity = '0';
            row.style.transform = 'translateX(30px)';
            setTimeout(() => row.remove(), 300);
        }
        this._agpCurrentTracks = (this._agpCurrentTracks || []).filter(t => t.id !== Number(trackId));
    },

    // Add-to-playlist popup (reusable from any context)
    async showAddToPlaylistPopup(trackId, btn) {
        event.stopPropagation();
        // Close any existing popup
        document.querySelectorAll('.add-pl-popup').forEach(p => p.remove());
        const playlists = await this.api('playlists');
        if (!playlists || playlists.length === 0) { alert('No playlists yet. Create one first from the Playlists page.'); return; }

        const popup = document.createElement('div');
        popup.className = 'add-pl-popup';
        popup.innerHTML = '<div class="add-pl-popup-title">Add to playlist</div>' +
            playlists.map(p => `<div class="add-pl-popup-item" onclick="App.addTrackToPlaylist(${p.id}, ${trackId}, this)">${this.esc(p.name)} <span style="opacity:.5;font-size:11px">(${p.trackCount})</span></div>`).join('');

        // Position near button
        const rect = btn.getBoundingClientRect();
        popup.style.top = Math.min(rect.bottom + 4, window.innerHeight - 320) + 'px';
        popup.style.left = Math.min(rect.left, window.innerWidth - 220) + 'px';
        document.body.appendChild(popup);

        // Close on outside click
        const close = (e) => { if (!popup.contains(e.target) && e.target !== btn) { popup.remove(); document.removeEventListener('click', close); } };
        setTimeout(() => document.addEventListener('click', close), 10);
    },

    async addTrackToPlaylist(playlistId, trackId, el) {
        const res = await this.apiPost(`playlists/${playlistId}/tracks`, { trackId });
        if (res && res.message && el) {
            el.classList.add('added');
            el.innerHTML = '&#10004; Added!';
            setTimeout(() => { const popup = el.closest('.add-pl-popup'); if (popup) popup.remove(); }, 800);
        }
    },

    // ─── Lyrics (LRCLIB API) ──────────────────────────────────
    async toggleLyrics() {
        let overlay = document.getElementById('lyrics-overlay');
        if (overlay && overlay.style.display === 'flex') {
            overlay.style.display = 'none';
            document.getElementById('btn-player-lyrics').style.color = '';
            return;
        }
        if (!this.currentTrack) return;
        document.getElementById('btn-player-lyrics').style.color = 'var(--accent)';

        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'lyrics-overlay';
            overlay.className = 'lyrics-overlay';
            overlay.innerHTML = `
                <div class="lyrics-panel">
                    <div class="lyrics-header">
                        <div class="lyrics-header-info">
                            <div class="lyrics-title" id="lyrics-title"></div>
                            <div class="lyrics-artist" id="lyrics-artist"></div>
                        </div>
                        <button class="lyrics-close" onclick="App.toggleLyrics()">&times;</button>
                    </div>
                    <div class="lyrics-body" id="lyrics-body"></div>
                    <div class="lyrics-footer">Lyrics provided by <a href="https://lrclib.net" target="_blank" style="color:var(--accent)">LRCLIB</a></div>
                </div>`;
            document.body.appendChild(overlay);
        }

        overlay.style.display = 'flex';
        const t = this.currentTrack;
        document.getElementById('lyrics-title').textContent = t.title || '';
        document.getElementById('lyrics-artist').textContent = t.artist || '';
        document.getElementById('lyrics-body').innerHTML = '<div class="lyrics-loading">Searching for lyrics...</div>';

        this._lyricsCache = this._lyricsCache || {};
        const cacheKey = `${t.artist}|${t.title}`.toLowerCase();

        if (this._lyricsCache[cacheKey]) {
            this._renderLyricsContent(this._lyricsCache[cacheKey]);
            return;
        }

        try {
            // Try exact match first
            const params = new URLSearchParams({ artist_name: t.artist || '', track_name: t.title || '' });
            if (t.album) params.set('album_name', t.album);
            const resp = await fetch(`https://lrclib.net/api/get?${params}`);

            if (resp.ok) {
                const data = await resp.json();
                this._lyricsCache[cacheKey] = data;
                this._renderLyricsContent(data);
                return;
            }

            // Fallback: search
            const searchParams = new URLSearchParams({ q: `${t.artist} ${t.title}` });
            const searchResp = await fetch(`https://lrclib.net/api/search?${searchParams}`);
            if (searchResp.ok) {
                const results = await searchResp.json();
                if (results && results.length > 0) {
                    this._lyricsCache[cacheKey] = results[0];
                    this._renderLyricsContent(results[0]);
                    return;
                }
            }

            document.getElementById('lyrics-body').innerHTML = '<div class="lyrics-not-found">No lyrics found for this track.</div>';
        } catch (err) {
            console.error('Lyrics fetch error:', err);
            document.getElementById('lyrics-body').innerHTML = '<div class="lyrics-not-found">Could not fetch lyrics. Check your connection.</div>';
        }
    },

    _renderLyricsContent(data) {
        const body = document.getElementById('lyrics-body');
        if (!body) return;
        // Prefer synced lyrics, fall back to plain
        const text = data.syncedLyrics || data.plainLyrics;
        if (!text) {
            body.innerHTML = '<div class="lyrics-not-found">Lyrics not available for this track.</div>';
            return;
        }

        let lines;
        if (data.syncedLyrics) {
            // Strip timestamps like [00:12.34] from synced lyrics for display
            lines = data.syncedLyrics.split('\n').map(l => l.replace(/^\[\d{2}:\d{2}\.\d{2,3}\]\s?/, ''));
        } else {
            lines = data.plainLyrics.split('\n');
        }
        body.innerHTML = '<div class="lyrics-text">' + lines.map(l => l.trim() === '' ? '<br>' : `<p>${this.esc(l)}</p>`).join('') + '</div>';
        body.scrollTop = 0;
    },

    // ─── Recently Added ──────────────────────────────────────
    async renderRecent(el) {
        const tracks = await this.api('tracks/recent?limit=50');
        let html = '<div class="page-header"><h1>Recently Added</h1></div>';
        if (tracks && tracks.length > 0) html += this.renderTrackTable(tracks);
        else html += this.emptyState('Nothing here yet', 'Scan your music library to populate this page.');
        el.innerHTML = html;
    },

    // ─── Most Played ─────────────────────────────────────────
    async renderMostPlayed(el) {
        const [tracks, videos, musicVideos, radio] = await Promise.all([
            this.api('tracks/mostplayed?limit=10'),
            this.api('videos/mostplayed?limit=10'),
            this.api('musicvideos/mostplayed?limit=10'),
            this.api('radio/mostplayed?limit=10')
        ]);

        let html = `<div class="page-header" style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:12px">
            <h1>${this.t('page.mostPlayed')}</h1>
            <button class="btn-secondary" style="font-size:12px;padding:6px 12px" onclick="App.resetPlayCounts()">Reset Play Counts</button>
        </div>`;

        const hasAny = (tracks?.length > 0) || (videos?.length > 0) || (musicVideos?.length > 0) || (radio?.length > 0);
        if (!hasAny) {
            html += this.emptyState(this.t('empty.noMostPlayed.title'), this.t('empty.noMostPlayed.desc'));
            el.innerHTML = html;
            return;
        }

        // ── Most Played Movies & TV ──
        if (videos && videos.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-film"/></svg> Movies & TV Shows</h2>
                    <button class="home-see-all" onclick="App.navigate('movies')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll home-row-wide">`;
            videos.forEach(v => {
                const thumbSrc = v.posterPath ? `/videometa/${v.posterPath}` : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
                const dur = this.formatDuration(v.duration);
                const typeLabel = v.mediaType === 'tv' ? 'TV' : v.mediaType === 'documentary' ? 'Doc' : v.mediaType === 'anime' ? 'Anime' : 'Movie';
                const subtitle = v.mediaType === 'tv' && v.seriesName
                    ? `${this.esc(v.seriesName)} S${(v.season||0).toString().padStart(2,'0')}E${(v.episode||0).toString().padStart(2,'0')}`
                    : (v.year ? String(v.year) : '');
                html += `<div class="mv-card home-card" onclick="App.openVideoDetail(${v.id})" data-video-id="${v.id}">
                    <div class="mv-card-thumb">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        <span class="video-type-badge video-type-${v.mediaType}">${typeLabel}</span>
                        <span class="play-count-badge">&#9654; ${v.playCount}</span>
                        <button class="mv-card-play" onclick="event.stopPropagation(); App.playVideo(${v.id})">&#9654;</button>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${subtitle}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Most Played Music ──
        if (tracks && tracks.length > 0) {
            this._mostPlayedTracks = tracks;
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-music"/></svg> Music</h2>
                    <button class="home-see-all" onclick="App.navigate('music')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll">`;
            tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                html += `<div class="song-card home-card" onclick="App.playMostPlayedTrack(${i})">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`}
                        <span class="play-count-badge">&#9654; ${t.playCount}</span>
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playMostPlayedTrack(${i})">&#9654;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta"><span>${this.esc(t.album)}</span><span>${dur}</span></div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Most Played Music Videos ──
        if (musicVideos && musicVideos.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-video"/></svg> Music Videos</h2>
                    <button class="home-see-all" onclick="App.navigate('musicvideos')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll home-row-wide">`;
            musicVideos.forEach(v => {
                const thumbSrc = v.thumbnailPath ? `/mvthumb/${v.thumbnailPath}` : '';
                const dur = this.formatDuration(v.duration);
                html += `<div class="mv-card home-card" onclick="App.openMvDetail(${v.id})" data-mv-id="${v.id}">
                    <div class="mv-card-thumb">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none">&#127909;</span>`
                            : `<span class="mv-card-placeholder">&#127909;</span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        <span class="play-count-badge">&#9654; ${v.playCount}</span>
                        <button class="mv-card-play" onclick="event.stopPropagation(); App.playMusicVideo(${v.id})">&#9654;</button>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${this.esc(v.artist)}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Most Played Radio ──
        if (radio && radio.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-radio"/></svg> Radio Stations</h2>
                    <button class="home-see-all" onclick="App.navigate('radio')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll">`;
            radio.forEach(s => {
                const logoSrc = s.logo ? `/radiologo/${s.logo}` : '';
                html += `<div class="song-card home-card" onclick="App.playRadioById(${s.id})">
                    <div class="song-card-art">
                        ${logoSrc
                            ? `<img src="${logoSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-radio"/></svg></span>`
                            : `<span class="song-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-radio"/></svg></span>`}
                        <span class="play-count-badge">&#9654; ${s.playCount}</span>
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playRadioById(${s.id})">&#9654;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(s.name)}</div>
                        <div class="song-card-artist">${this.esc(s.genre)} &middot; ${this.esc(s.country)}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        el.innerHTML = html;
    },

    playMostPlayedTrack(index) {
        if (!this._mostPlayedTracks || !this._mostPlayedTracks[index]) return;
        this.playlist = this._mostPlayedTracks;
        this.playIndex = index;
        this.playTrack(this._mostPlayedTracks[index]);
    },

    async resetPlayCounts() {
        if (!confirm('Reset all play counts? This cannot be undone.')) return;
        const res = await this.apiPost('playcounts/reset', {});
        if (res && res.success) this.navigate('mostplayed');
    },

    // ─── Rescan Folders ──────────────────────────────────────
    async renderRescan(el) {
        const icon = (id) => `<svg><use href="#icon-${id}"/></svg>`;
        const primaryBtn = (label, onclick, iconId) =>
            `<button class="rescan-btn rescan-btn-primary" onclick="${onclick}">${icon(iconId)} ${label}</button>`;
        const secondaryBtn = (label, onclick, iconId) =>
            `<button class="rescan-btn rescan-btn-secondary" onclick="${onclick}">${icon(iconId)} ${label}</button>`;

        const card = (iconId, title, desc, actions, statusId) => `
            <div class="rescan-card">
                <div class="rescan-card-header">
                    <div class="rescan-card-icon">${icon(iconId)}</div>
                    <h3 class="rescan-card-title">${title}</h3>
                </div>
                <p class="rescan-card-desc">${desc}</p>
                <div class="rescan-card-actions">${actions}</div>
                <div class="rescan-status" id="${statusId}"></div>
            </div>`;

        let html = `<div class="page-header"><h1>${this.t('page.rescan')}</h1></div>`;
        html += `<div class="rescan-grid">`;

        html += card('music',
            this.t('rescan.musicLibraryScan'),
            this.t('rescan.musicDesc'),
            primaryBtn(this.t('btn.startMusicScan'), 'App.startScan()', 'scan'),
            'scan-status');

        html += card('image',
            this.t('rescan.picturesLibraryScan'),
            this.t('rescan.picturesDesc'),
            primaryBtn(this.t('btn.startPicturesScan'), 'App.startPictureScan()', 'scan'),
            'pic-scan-status');

        html += card('book',
            this.t('rescan.ebooksLibraryScan'),
            this.t('rescan.ebooksDesc'),
            primaryBtn(this.t('btn.startEBooksScan'), 'App.startEBookScan()', 'scan'),
            'ebook-scan-status');

        html += card('headphones',
            this.t('rescan.audioBooksLibraryScan'),
            this.t('rescan.audioBooksDesc'),
            primaryBtn(this.t('btn.startAudioBooksScan'), 'App.startAudioBookScan()', 'scan'),
            'audiobook-scan-status');

        html += card('video',
            this.t('rescan.musicVideosLibraryScan'),
            this.t('rescan.musicVideosDesc'),
            primaryBtn(this.t('btn.startMusicVideosScan'), 'App.startMvScan()', 'scan') +
            secondaryBtn(this.t('btn.generateThumbnails'), 'App.generateMvThumbnails()', 'image'),
            'mv-scan-status');

        html += card('film',
            this.t('rescan.moviesTvScan'),
            this.t('rescan.moviesTvDesc'),
            primaryBtn(this.t('btn.startMoviesTvScan'), 'App.startVideoScan()', 'scan') +
            secondaryBtn(this.t('nav.movies') + ' only', "App.startVideoScan('movies')", 'film') +
            secondaryBtn(this.t('nav.tvShows') + ' only', "App.startVideoScan('tvshows')", 'tv') +
            secondaryBtn(this.t('btn.generateThumbnails'), 'App.generateVideoThumbnails()', 'image') +
            secondaryBtn('Refresh HDR Metadata', 'App.refreshHdrMetadata()', 'refresh') +
            secondaryBtn(this.t('btn.fetchMissing'), 'App.fetchAllVideoMetadata(false)', 'download') +
            secondaryBtn(this.t('btn.refetchAll'), 'App.fetchAllVideoMetadata(true)', 'refresh'),
            'video-scan-status');

        html += card('film',
            this.t('nav.anime'),
            'Scan your Anime folders and fetch metadata from MyAnimeList via Jikan.',
            primaryBtn('Start Anime Scan', 'App.startAnimeScan()', 'scan'),
            'anime-scan-status');

        html += `</div>`;
        el.innerHTML = html;
    },

    // ─── Settings Page ───────────────────────────────────────
    async renderSettings(el) {
        const config = await this.api('config/info');
        if (!config) { el.innerHTML = this.emptyState('Error', 'Could not load configuration.'); return; }

        const isAdmin = this.userRole === 'admin';
        const toggle = (id, checked) => `<label class="setting-toggle"><input type="checkbox" id="cfg-${id}"${checked ? ' checked' : ''}${!isAdmin ? ' disabled' : ''}><span class="toggle-track"><span class="toggle-thumb"></span></span></label>`;
        const input = (id, val, type = 'text', cls = '') => `<input type="${type}" id="cfg-${id}" class="setting-input ${cls}" value="${this.esc(val + '')}"${!isAdmin ? ' disabled' : ''}>`;
        const select = (id, val, opts) => `<select id="cfg-${id}" class="setting-select"${!isAdmin ? ' disabled' : ''}>${opts.map(o => `<option value="${o.v}"${val === o.v ? ' selected' : ''}>${o.l}</option>`).join('')}</select>`;

        let html = `<div class="page-header"><h1>${this.t('page.settings')}</h1></div>`;

        // ── Resource Monitor ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-activity"/></svg> ${this.t('settings.resourceMonitor')}</h3>
            <p class="settings-section-hint">${this.t('settings.resourceMonitorHint')}</p>
            <div class="metrics-charts-row">
                <div class="metrics-chart-card">
                    <div class="metrics-chart-header">
                        <div class="metrics-chart-title-group">
                            <span class="metrics-chart-title">${this.t('settings.cpu')}</span>
                            <span class="metrics-legend"><span class="metrics-legend-dot" style="background:#4d8bf5"></span>System <span class="metrics-legend-dot" style="background:#2dd4bf"></span>NexusM</span>
                        </div>
                        <div class="metrics-badge-group">
                            <span class="metrics-badge" id="metrics-cpu-sys-badge">—</span>
                            <span class="metrics-badge" style="color:#2dd4bf" id="metrics-cpu-proc-badge">—</span>
                        </div>
                    </div>
                    <canvas id="metrics-cpu-canvas" class="metrics-canvas"></canvas>
                </div>
                <div class="metrics-chart-card">
                    <div class="metrics-chart-header">
                        <div class="metrics-chart-title-group">
                            <span class="metrics-chart-title">${this.t('settings.ram')}</span>
                            <span class="metrics-legend"><span class="metrics-legend-dot" style="background:#4d8bf5"></span>System <span class="metrics-legend-dot" style="background:#2dd4bf"></span>NexusM</span>
                        </div>
                        <div class="metrics-badge-group">
                            <span class="metrics-badge" id="metrics-ram-sys-badge">—</span>
                            <span class="metrics-badge" style="color:#2dd4bf" id="metrics-ram-proc-badge">—</span>
                        </div>
                    </div>
                    <canvas id="metrics-ram-canvas" class="metrics-canvas"></canvas>
                </div>
                <div class="metrics-chart-card">
                    <div class="metrics-chart-header">
                        <div class="metrics-chart-title-group">
                            <span class="metrics-chart-title">${this.t('settings.network')}</span>
                            <span class="metrics-legend"><span class="metrics-legend-dot" style="background:#fb923c"></span>▲ TX <span class="metrics-legend-dot" style="background:#4ade80"></span>▼ RX</span>
                        </div>
                        <div class="metrics-badge-group">
                            <span class="metrics-badge" style="color:#fb923c" id="metrics-net-tx-badge">—</span>
                            <span class="metrics-badge" style="color:#4ade80" id="metrics-net-rx-badge">—</span>
                        </div>
                    </div>
                    <canvas id="metrics-net-canvas" class="metrics-canvas"></canvas>
                </div>
            </div>
        </div>`;

        // ── Server Information ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-settings"/></svg> ${this.t('settings.serverInformation')}</h3>
            <div class="setting-row"><span class="setting-label">${this.t('settings.version')}</span><span class="setting-value">${config.version}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.platform')}</span><span class="setting-value">${this.esc(config.platform)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.framework')}</span><span class="setting-value">.NET ${this.esc(config.framework)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.configFile')}</span><span class="setting-value" style="font-size:12px;word-break:break-all">${this.esc(config.configFile)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.welcomePopup')}</span><span class="setting-value"><label class="toggle-switch"><input type="checkbox" id="welcome-popup-toggle" ${this._welcomeDismissed ? '' : 'checked'} onchange="App.toggleWelcomePopup(this.checked)"><span class="toggle-slider"></span></label> <span class="setting-hint">${this.t('settings.welcomePopupHint')}</span></span></div>
            <div class="setting-group-label">${this.t('settings.serverSettings')} <span class="setting-hint">(${this.t('settings.changesRequireRestart')})</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.serverHost')}</span><span class="setting-value">${input('serverHost', config.serverHost)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.serverPort')}</span><span class="setting-value">${input('serverPort', config.serverPort, 'number')}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.workerThreads')}</span><span class="setting-value">${input('workerThreads', config.workerThreads, 'number')} <span class="setting-hint">${this.t('settings.autoHint')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.requestTimeout')}</span><span class="setting-value">${input('requestTimeout', config.requestTimeout, 'number')}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.sessionTimeout')}</span><span class="setting-value">${input('sessionTimeout', config.sessionTimeout, 'number')}</span></div>
            <div class="setting-group-label">${this.t('settings.startupOptions')} <span class="setting-hint">(${this.t('settings.changesRequireRestart')})</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.showConsoleWindow')}</span><span class="setting-value">${toggle('showConsole', config.showConsole)} <span class="setting-hint">${this.t('settings.hideToRunInBackground')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.openBrowserOnStart')}</span><span class="setting-value">${toggle('openBrowser', config.openBrowser)} <span class="setting-hint">${this.t('settings.autoLaunchWebUI')}</span></span></div>
        <div class="setting-row"><span class="setting-label">${config.isLinux ? 'Run on Startup' : 'Run on Windows Startup'}</span><span class="setting-value">${toggle('runOnStartup', config.runOnStartup)} <span class="setting-hint">${config.isLinux ? 'Automatically start NexusM via systemd service at boot' : 'Automatically start NexusM when Windows starts'}</span></span></div>
        </div>`;

        // ── Trakt.tv ──
        {
            const traktConnected = config.traktConnected;
            const traktUser = config.traktUsername || '';
            html += `<div class="settings-section" id="settings-trakt-section">
                <h3><svg class="settings-icon"><use href="#icon-radio"/></svg> ${this.t('settings.traktSection')}</h3>
                <div class="setting-hint" style="margin-bottom:12px">${this.t('settings.traktGetAppKey')}</div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.traktClientId')}</span><span class="setting-value">${input('traktClientId', config.traktClientId || '', 'text', 'setting-input-wide')}</span></div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.traktClientSecret')}</span><span class="setting-value">${input('traktClientSecret', config.traktClientSecret || '', 'password', 'setting-input-wide')}</span></div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.traktScrobble')}</span><span class="setting-value">${toggle('traktScrobbleEnabled', config.traktScrobbleEnabled)} <span class="setting-hint">${this.t('settings.traktScrobbleHint')}</span></span></div>
                <div class="setting-row">
                    <span class="setting-label">${this.t('settings.traktStatus')}</span>
                    <span class="setting-value" id="trakt-status-cell">
                        ${traktConnected
                            ? `<span class="trakt-connected-badge">&#9679; ${this.t('settings.traktConnectedAs')} <strong>${this.esc(traktUser)}</strong></span>
                               <button class="btn-secondary trakt-action-btn" onclick="App.traktDisconnect()">${this.t('btn.traktDisconnect')}</button>`
                            : `<span style="color:var(--text-secondary)">&#9675; ${this.t('settings.traktNotConnected')}</span>
                               <button class="btn-secondary trakt-action-btn" onclick="App.traktConnect()">${this.t('btn.traktConnect')}</button>`
                        }
                    </span>
                </div>
                <div id="trakt-pin-panel" style="display:none" class="trakt-pin-panel">
                    <div style="margin-bottom:10px">
                        <div class="setting-hint" style="margin-bottom:4px"><strong>Step 1</strong> — Enter this code on Trakt&rsquo;s website:</div>
                        <div class="trakt-pin-code" id="trakt-pin-code"></div>
                        <div class="setting-hint">${this.t('settings.traktPinInstruction')} <a href="https://trakt.tv/activate" target="_blank" rel="noopener">trakt.tv/activate</a></div>
                    </div>
                    <div style="margin-top:12px;padding-top:12px;border-top:1px solid var(--border)">
                        <div class="setting-hint" style="margin-bottom:6px"><strong>Step 2</strong> — Enter the PIN shown by Trakt&rsquo;s website:</div>
                        <div style="display:flex;gap:8px;align-items:center">
                            <input id="trakt-auth-pin" type="text" class="setting-input" placeholder="e.g. 628569E2" maxlength="12" style="width:160px;letter-spacing:2px;text-transform:uppercase">
                            <button class="btn-secondary trakt-action-btn" onclick="App.traktSubmitPin()">Authorize</button>
                        </div>
                    </div>
                    <div class="setting-hint" id="trakt-pin-status" style="margin-top:8px;color:var(--text-secondary)">${this.t('settings.traktConnecting')}</div>
                </div>
            </div>`;
        }

        // ── DLNA ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-tv"/></svg> ${this.t('settings.dlna')}</h3>
            <div class="setting-group-label">${this.t('settings.changesRequireRestart')}</div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.dlnaEnabled')}</span><span class="setting-value">${toggle('dlnaEnabled', config.dlnaEnabled)} <span class="setting-hint">${this.t('settings.dlnaEnabledHint')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.dlnaFriendlyName')}</span><span class="setting-value">${input('dlnaFriendlyName', config.dlnaFriendlyName || 'NexusM Media Server', 'text', 'setting-input')} <span class="setting-hint">${this.t('settings.dlnaFriendlyNameHint')}</span></span></div>
        </div>`;

        // ── HTTPS / TLS ──
        {
            const certStatusLabel = config.httpsCertStatus === 'auto'
                ? `<span style="color:var(--success)">${this.t('settings.httpsCertAuto')}</span> &middot; ${this.t('settings.httpsCertExpiry')}: ${config.httpsCertExpiry || '?'}`
                : config.httpsCertStatus === 'custom'
                ? `<span style="color:var(--accent)">${this.t('settings.httpsCertCustom')}</span> &middot; ${this.t('settings.httpsCertExpiry')}: ${config.httpsCertExpiry || '?'}`
                : config.httpsCertStatus === 'custom-missing'
                ? `<span style="color:var(--danger)">${this.t('settings.httpsCertCustomMissing')}</span>`
                : `<span style="color:var(--text-muted)">${this.t('settings.httpsCertNone')}</span>`;

            html += `<div class="settings-section">
                <h3><svg class="settings-icon"><use href="#icon-lock"/></svg> ${this.t('settings.https')}</h3>
                <div class="setting-group-label">${this.t('settings.changesRequireRestart')}</div>
                <div class="setting-row">
                    <span class="setting-label">${this.t('settings.httpsCertActions')}</span>
                    <span class="setting-value" style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
                        <button class="btn-action" onclick="App.downloadCert()" style="font-size:12px;padding:4px 12px;background:var(--accent);color:#fff;border:none;border-radius:6px;cursor:pointer">
                            <svg style="width:13px;height:13px;stroke:currentColor;fill:none;stroke-width:2;margin-right:4px;vertical-align:middle"><use href="#icon-download"/></svg>${this.t('settings.httpsDownloadCert')}
                        </button>
                        <button class="btn-action" id="btn-regen-cert" onclick="App.regenerateCert()" style="font-size:12px;padding:4px 12px;background:var(--accent);color:#fff;border:none;border-radius:6px;cursor:pointer">
                            <svg style="width:13px;height:13px;stroke:currentColor;fill:none;stroke-width:2;margin-right:4px;vertical-align:middle"><use href="#icon-refresh"/></svg>${this.t('settings.httpsRegenCert')}
                        </button>
                        <span class="setting-hint">${this.t('settings.httpsDownloadCertHint')}</span>
                    </span>
                </div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.httpsEnabled')}</span><span class="setting-value">${toggle('httpsEnabled', config.httpsEnabled)}</span></div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.httpsPort')}</span><span class="setting-value">${input('httpsPort', config.httpsPort, 'number')} <span class="setting-hint">${this.t('settings.httpsPortHint')}</span></span></div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.httpsRedirect')}</span><span class="setting-value">${toggle('httpsRedirectHttp', config.httpsRedirectHttp)} <span class="setting-hint">${this.t('settings.httpsRedirectHint')}</span></span></div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.httpsCertStatus')}</span><span class="setting-value">${certStatusLabel}</span></div>
                <div class="setting-group-label">${this.t('settings.httpsCustomCertLabel')}</div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.httpsCertPath')}</span><span class="setting-value"><code style="background:var(--bg-secondary,#2a2a2a);padding:3px 8px;border-radius:4px;font-size:13px;color:var(--accent)">/assets/certs</code> <span class="setting-hint">${this.t('settings.httpsCertPathHint')}</span></span></div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.httpsCertPassword')}</span><span class="setting-value"><input type="password" id="cfg-httpsCertPassword" class="setting-input" placeholder="${this.t('settings.httpsCertPasswordHint')}" ${!isAdmin ? 'disabled' : ''}></span></div>
            </div>`;
        }

        // ── User Management (admin only) ──
        if (isAdmin) {
            const users = await this.api('auth/users');
            this._adminUsers = users || [];
            html += `<div class="settings-section">
                <h3><svg class="settings-icon"><use href="#icon-users"/></svg> ${this.t('settings.userManagement')}</h3>`;

            if (users && users.length > 0) {
                html += `<table class="settings-shares-table"><thead><tr>
                    <th>${this.t('table.username')}</th><th>${this.t('table.displayName')}</th><th>${this.t('table.role')}</th><th>${this.t('table.lastLogin')}</th><th style="text-align:right">${this.t('settings.actions')}</th>
                </tr></thead><tbody>`;
                users.forEach(u => {
                    const lastLogin = u.lastLogin ? new Date(u.lastLogin).toLocaleString() : this.t('settings.never');
                    const isSelf = u.username === this.userName;
                    const roleBadge = u.role === 'admin'
                        ? `<span class="settings-badge" style="background:#e67e22">${this.t('settings.admin')}</span>`
                        : u.role === 'child'
                        ? `<span class="settings-badge" style="background:#22c55e">${this.t('settings.child')}</span>`
                        : `<span class="settings-badge" style="background:var(--accent)">${this.t('settings.guest')}</span>`;
                    html += `<tr id="user-row-${u.id}" data-child-settings='${(u.childSettings || '').replace(/'/g, '&#39;')}'>
                        <td style="font-weight:500">${this.esc(u.username)}</td>
                        <td>${this.esc(u.displayName || '-')}</td>
                        <td>${roleBadge}</td>
                        <td style="font-size:12px;color:var(--text-muted)">${lastLogin}</td>
                        <td class="user-mgmt-actions">
                            <button class="user-mgmt-btn" onclick="App.showEditUser(${u.id}, '${this.esc(u.username)}', '${this.esc(u.displayName || '')}', '${u.role}')">${this.t('btn.edit')}</button>
                            ${isSelf ? '' : `<button class="user-mgmt-btn user-mgmt-btn-danger" onclick="App.deleteUser(${u.id}, '${this.esc(u.username)}')">${this.t('btn.delete')}</button>`}
                        </td>
                    </tr>`;
                });
                html += '</tbody></table>';
            } else {
                html += `<p style="color:var(--text-muted);text-align:center;padding:20px">${this.t('empty.noUsersFound')}</p>`;
            }

            html += `<div id="user-mgmt-form-area"></div>
                <div style="margin-top:12px">
                    <button class="user-mgmt-btn user-mgmt-btn-add" onclick="App.showAddUser()">+ ${this.t('btn.addUser')}</button>
                </div>
            </div>`;
        }

        // ── Security ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-lock"/></svg> ${this.t('settings.security')}</h3>
            <div class="setting-row"><span class="setting-label">${this.t('settings.pinAuthentication')}</span><span class="setting-value">${toggle('securityByPin', config.securityByPin)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.defaultAdminUser')}</span><span class="setting-value">${input('defaultAdminUser', config.defaultAdminUser)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.ipWhitelist')}</span><span class="setting-value">${input('ipWhitelist', config.ipWhitelist, 'text', 'setting-input-wide')} <span class="setting-hint">${this.t('settings.ipWhitelistHint')}</span></span></div>
        </div>`;

        // ── UI Preferences ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-layers"/></svg> ${this.t('settings.uiPreferences')}</h3>
            <div class="setting-row"><span class="setting-label">${this.t('settings.goBigDefault')}</span><span class="setting-value">${toggle('goBigDefault', config.goBigDefault)} <span class="setting-hint">${this.t('settings.goBigDefaultHint')}</span></span></div>
            <div class="setting-row" style="display:block;padding:6px 0 2px">
                <span class="setting-hint" style="display:block;line-height:1.55">
                    <strong>Go Big TV tip:</strong> For seamless fullscreen from the mobile remote, run NexusM in a dedicated browser window &mdash;
                    install as a <strong>Web App</strong> (click the install icon in your browser&rsquo;s address bar), or launch Chrome with
                    <code style="font-size:0.78rem;background:var(--bg-active);padding:1px 5px;border-radius:3px">--app=https://your-nexusm-url</code>
                    or <code style="font-size:0.78rem;background:var(--bg-active);padding:1px 5px;border-radius:3px">--kiosk https://your-nexusm-url</code> for dedicated TV mode.
                </span>
            </div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.theme')}</span><span class="setting-value">${select('theme', config.theme, [{v:'dark',l:this.t('settings.dark')},{v:'blue',l:this.t('settings.blue')},{v:'purple',l:this.t('settings.purple')},{v:'emerald',l:this.t('settings.emerald')},{v:'sky-grey',l:this.t('settings.skyGrey')},{v:'sky-blue',l:this.t('settings.skyBlue')}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.defaultView')}</span><span class="setting-value">${select('defaultView', config.defaultView, [{v:'grid',l:this.t('settings.grid')},{v:'list',l:this.t('settings.list')}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.language')}</span><span class="setting-value">${select('language', config.language, [{v:'en',l:'English'},{v:'fr',l:'Francais'},{v:'de',l:'Deutsch'},{v:'es',l:'Espanol'},{v:'it',l:'Italiano'},{v:'nl',l:'Nederlands'},{v:'pl',l:'Polski'},{v:'pt',l:'Portugues'},{v:'ro',l:'Română'},{v:'ru',l:'Русский'},{v:'sv',l:'Svenska'},{v:'uk',l:'Українська'},{v:'sl',l:'Slovenščina'},{v:'et',l:'Eesti'},{v:'fi',l:'Suomi'},{v:'no',l:'Norsk'},{v:'lt',l:'Lietuvių'},{v:'sr',l:'Српски'},{v:'sq',l:'Shqip'}])}</span></div>
        </div>`;

        // ── UI Templates ──
        html += `<div class="settings-section" id="settings-templates-section">
            <h3><svg class="settings-icon"><use href="#icon-layers"/></svg> ${this.t('settings.uiTemplates')}</h3>
            <p class="settings-section-hint">${this.t('settings.uiTemplatesHint')}</p>
            <div id="tpl-grid" style="display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px;margin-top:12px">
                <div class="tpl-card ${!config.uiTemplate ? 'tpl-card--active' : ''}" onclick="App._selectTemplate('')" data-tpl-id="">
                    <div class="tpl-card-preview tpl-preview-default">
                        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-layers"/></svg>
                    </div>
                    <div class="tpl-card-body">
                        <div class="tpl-card-name">${this.t('settings.uiTemplateDefault')}</div>
                        <div class="tpl-card-desc">${this.t('settings.uiTemplateDefaultDesc')}</div>
                        <div class="tpl-card-author">NexusM</div>
                    </div>
                </div>
            </div>
            <input type="hidden" id="cfg-uiTemplate" value="${this.esc(config.uiTemplate || '')}">
            <p class="settings-section-hint" style="margin-top:10px">${this.t('settings.uiTemplatesFolder')} <code>wwwroot/templates/</code></p>
        </div>`;

        // ── Deep Media Analysis ──
        if (isAdmin) {
            html += `<div class="settings-section">
                <h3><svg class="settings-icon"><use href="#icon-activity"/></svg> ${this.t('settings.deepScan')}</h3>
                <p class="settings-section-hint">${this.t('settings.deepScanHint')}</p>
                <div class="setting-row"><span class="setting-label">${this.t('settings.deepScanEnabled')}</span><span class="setting-value">${toggle('deepScanEnabled', config.deepScanEnabled)}</span></div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.deepScanInterval')}</span><span class="setting-value">${input('deepScanIntervalMinutes', config.deepScanIntervalMinutes ?? 60, 'number', 'setting-input-sm')} <span class="setting-hint">${this.t('settings.deepScanIntervalHint')}</span></span></div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.deepScanDelay')}</span><span class="setting-value">${input('deepScanDelayMs', config.deepScanDelayMs ?? 1500, 'number', 'setting-input-sm')} <span class="setting-hint">${this.t('settings.deepScanDelayHint')}</span></span></div>
                <div class="setting-row"><span class="setting-label">${this.t('settings.deepScanBatch')}</span><span class="setting-value">${input('deepScanBatchSize', config.deepScanBatchSize ?? 20, 'number', 'setting-input-sm')} <span class="setting-hint">${this.t('settings.deepScanBatchHint')}</span></span></div>
            </div>`;
        }

        // ── Menu Components ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-menu"/></svg> ${this.t('settings.menuComponents')}</h3>`;
        const menuItems = [
            { label: this.t('nav.music'), key: 'showMusic', icon: 'music' },
            { label: this.t('nav.pictures'), key: 'showPictures', icon: 'image' },
            { label: this.t('nav.movies'), key: 'showMovies', icon: 'film' },
            { label: this.t('nav.tvShows'), key: 'showTvShows', icon: 'tv' },
            { label: this.t('settings.musicVideos'), key: 'showMusicVideos', icon: 'video' },
            { label: this.t('settings.radio'), key: 'showRadio', icon: 'radio' },
            { label: this.t('settings.internetTv'), key: 'showInternetTV', icon: 'tv' },
            { label: this.t('settings.ebooks'), key: 'showEBooks', icon: 'book' },
            { label: this.t('settings.audioBooks'), key: 'showAudioBooks', icon: 'headphones' },
            { label: this.t('settings.actors'), key: 'showActors', icon: 'users' },
            { label: this.t('settings.podcasts'), key: 'showPodcasts', icon: 'podcast' },
            { label: this.t('nav.anime'), key: 'showAnime', icon: 'film' }
        ];
        menuItems.forEach(m => {
            html += `<div class="setting-row">
                <span class="setting-label" style="display:flex;align-items:center;gap:6px">
                    <svg style="width:16px;height:16px;stroke:var(--text-secondary);fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-${m.icon}"/></svg>
                    ${m.label}
                </span>
                <span class="setting-value">${toggle(m.key, config[m.key])}</span>
            </div>`;
        });
        html += '</div>';

        // ── Media Shares ──
        const folderTypes = [
            { key: 'musicFolders', type: 'music', label: this.t('settings.musicFolders') },
            { key: 'moviesFolders', type: 'movies', label: this.t('settings.moviesFolders') },
            { key: 'tvShowsFolders', type: 'tvshows', label: this.t('settings.tvShowsFolders') },
            { key: 'musicVideosFolders', type: 'musicvideos', label: this.t('settings.musicVideosFolders') },
            { key: 'picturesFolders', type: 'pictures', label: this.t('settings.picturesFolders') },
            { key: 'ebooksFolders', type: 'ebooks', label: this.t('settings.ebooksFolders') },
            { key: 'audioBooksFolders', type: 'audiobooks', label: this.t('settings.audioBooksFolders') },
            { key: 'animeFolders', type: 'anime', label: this.t('settings.animeFolders') }
        ];
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-folder"/></svg> ${this.t('settings.libraryFolders')}</h3>
            <p class="setting-hint" style="margin-bottom:12px">${this.t('settings.libraryFoldersHint')}</p>`;
        folderTypes.forEach(f => {
            html += `<div class="setting-row setting-row-col" style="margin-bottom:4px">
                <span class="setting-label">${f.label}</span>
                <span class="setting-value" style="display:flex;gap:6px;align-items:center">
                    ${input(f.key, config[f.key], 'text', 'setting-input-wide')}
                    <button class="btn-action" style="font-size:11px;padding:4px 8px;white-space:nowrap" onclick="App.toggleShareCredentials('${f.type}')" title="${this.t('btn.credentials', 'Credentials')}">&#128274;</button>
                </span>
            </div>
            <div id="share-creds-${f.type}" style="display:none"></div>`;
        });
        html += `<div style="margin:8px 0 12px 0"><button class="btn-action" style="font-size:12px" onclick="App.mountAllShares()">${this.t('btn.mountAll', 'Mount All Shares')}</button></div>
            <div class="setting-group-label">${this.t('settings.scanSettings')}</div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.autoScanOnStartup')}</span><span class="setting-value">${toggle('autoScanOnStartup', config.autoScanOnStartup)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.autoScanInterval')}</span><span class="setting-value">${input('autoScanInterval', config.autoScanInterval, 'number')} <span class="setting-hint">${this.t('settings.manualOnlyHint')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.scanThreads')}</span><span class="setting-value">${input('scanThreads', config.scanThreads, 'number')}</span></div>
        </div>`;

        // ── File Extensions ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-file"/></svg> ${this.t('settings.fileExtensions')}</h3>
            <p class="setting-hint" style="margin-bottom:12px">${this.t('settings.fileExtensionsHint')}</p>
            <div class="setting-row setting-row-col"><span class="setting-label">${this.t('settings.audioExtensions')}</span><span class="setting-value">${input('audioExtensions', config.audioExtensions, 'text', 'setting-input-wide')}</span></div>
            <div class="setting-row setting-row-col"><span class="setting-label">${this.t('settings.videoExtensions')}</span><span class="setting-value">${input('videoExtensions', config.videoExtensions, 'text', 'setting-input-wide')}</span></div>
            <div class="setting-row setting-row-col"><span class="setting-label">${this.t('settings.musicVideoExtensions')}</span><span class="setting-value">${input('musicVideoExtensions', config.musicVideoExtensions, 'text', 'setting-input-wide')}</span></div>
            <div class="setting-row setting-row-col"><span class="setting-label">${this.t('settings.imageExtensions')}</span><span class="setting-value">${input('imageExtensions', config.imageExtensions, 'text', 'setting-input-wide')}</span></div>
            <div class="setting-row setting-row-col"><span class="setting-label">${this.t('settings.ebookExtensions')}</span><span class="setting-value">${input('ebookExtensions', config.ebookExtensions, 'text', 'setting-input-wide')}</span></div>
            <div class="setting-row setting-row-col"><span class="setting-label">${this.t('settings.audioBooksExtensions')}</span><span class="setting-value">${input('audioBooksExtensions', config.audioBooksExtensions, 'text', 'setting-input-wide')}</span></div>
        </div>`;

        // ── Metadata Providers ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-film"/></svg> ${this.t('settings.metadataProviders')}</h3>
            <p style="color:var(--text-muted);font-size:12px;margin-bottom:12px">${this.t('settings.metadataProvidersDesc')}</p>
            <div class="setting-row"><span class="setting-label">${this.t('settings.provider')}</span><span class="setting-value">${select('metadataProvider', config.metadataProvider, [{v:'tvmaze',l:this.t('settings.tvmazeFree')},{v:'tmdb',l:this.t('settings.tmdbApiKeyOption')},{v:'none',l:this.t('settings.none')}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.tmdbApiKey')}</span><span class="setting-value">${input('tmdbApiKey', '', 'password', 'setting-input-wide')}${config.tmdbApiKey ? ` <span class="setting-hint" style="color:var(--success);margin-left:8px">&#10003; Key is set</span>` : ''} <a href="https://www.themoviedb.org/settings/api" target="_blank" style="color:var(--accent);font-size:11px;margin-left:8px">${this.t('settings.getFreeKey')}</a></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.watchmodeApiKey')}</span><span class="setting-value">${input('watchmodeApiKey', '', 'password', 'setting-input-wide')}${config.watchmodeApiKey ? ` <span class="setting-hint" style="color:var(--success);margin-left:8px">&#10003; Key is set</span>` : ''} <a href="https://api.watchmode.com" target="_blank" style="color:var(--accent);font-size:11px;margin-left:8px">${this.t('settings.getFreeKey')}</a><span class="setting-hint" style="margin-left:8px">1,000 req/month free</span></span></div>

            <div class="setting-row"><span class="setting-label">${this.t('settings.fetchOnScan')}</span><span class="setting-value">${toggle('fetchMetadataOnScan', config.fetchMetadataOnScan)} <span class="setting-hint" style="margin-left:8px">${this.t('settings.autoFetchMetadata')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.castPhotos')}</span><span class="setting-value">${toggle('fetchCastPhotos', config.fetchCastPhotos)} <span class="setting-hint" style="margin-left:8px">${this.t('settings.downloadActorPhotos')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.manualFetch')}</span><span class="setting-value" style="display:flex;align-items:center;gap:8px;flex-wrap:wrap">
                <button class="rescan-btn rescan-btn-primary" onclick="App.fetchAllVideoMetadata(false)"><svg><use href="#icon-download"/></svg> ${this.t('btn.fetchMissing')}</button>
                <button class="rescan-btn rescan-btn-secondary" onclick="App.fetchAllVideoMetadata(true)"><svg><use href="#icon-refresh"/></svg> ${this.t('btn.refetchAll')}</button>
                <span id="metadata-fetch-status" class="setting-hint"></span>
            </span></div>
        </div>`;

        // ── Subtitles ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-file-text"/></svg> ${this.t('settings.subtitles')}</h3>
            <p style="color:var(--text-muted);font-size:12px;margin-bottom:12px">${this.t('settings.subtitlesDesc')}</p>
            <div class="setting-row"><span class="setting-label">${this.t('settings.osApiKey')}</span><span class="setting-value">${input('openSubtitlesApiKey', config.openSubtitlesApiKey || '', 'password', 'setting-input-wide')} <a href="https://www.opensubtitles.com/consumers" target="_blank" style="color:var(--accent);font-size:11px;margin-left:8px">${this.t('settings.getFreeKey')}</a></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.osUsername')}</span><span class="setting-value">${input('openSubtitlesUsername', config.openSubtitlesUsername || '', 'text', 'setting-input')}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.osPassword')}</span><span class="setting-value">${input('openSubtitlesPassword', '', 'password', 'setting-input')}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.subDlApiKey')}</span><span class="setting-value">${input('subDlApiKey', config.subDlApiKey || '', 'password', 'setting-input-wide')} <a href="https://subdl.com" target="_blank" style="color:var(--accent);font-size:11px;margin-left:8px">${this.t('settings.getFreeKey')}</a></span></div>
        </div>`;

        // ── Playback & Tools ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-video"/></svg> ${this.t('settings.playbackTools')}</h3>
            <div class="setting-row"><span class="setting-label">${this.t('settings.transcodingEnabled')}</span><span class="setting-value">${toggle('transcodingEnabled', config.transcodingEnabled)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.introSkipper')}</span><span class="setting-value">${toggle('introSkipperEnabled', config.introSkipperEnabled)} <span class="setting-hint">${this.t('settings.introSkipperHint')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.transcodeFormat')}</span><span class="setting-value">${select('transcodeFormat', config.transcodeFormat, [{v:'mp3',l:'MP3'},{v:'aac',l:'AAC'},{v:'opus',l:'Opus'}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.transcodeBitrate')}</span><span class="setting-value">${select('transcodeBitrate', config.transcodeBitrate, [{v:'128k',l:'128 kbps'},{v:'192k',l:'192 kbps'},{v:'256k',l:'256 kbps'},{v:'320k',l:'320 kbps'}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.ffmpegPath')}</span><span class="setting-value">${input('ffmpegPath', config.ffmpegPath || '', 'text', 'setting-input-wide')} <span class="setting-hint">${this.t('settings.emptyAutoDetect')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.ffmpegStatus')}</span><span class="setting-value">${config.ffmpegAvailable
                ? `<span style="color:var(--success)">${this.t('status.available')}</span>`
                : `<span style="color:var(--warning)">${this.t('status.notInstalled')}</span>
                   <span class="setting-hint" style="display:block;margin-top:4px">${this.t('settings.ffmpegNotFoundHint')}</span>`}</span></div>
            ${config.ffmpegAvailable ? `<div class="setting-row"><span class="setting-label">${this.t('settings.ffmpegVersion')}</span><span class="setting-value"><code style="font-size:0.85em">${config.ffmpegVersion || 'unknown'}</code></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.ffmpegFeatures')}</span><span class="setting-value"><div class="ffmpeg-caps"><span class="ffmpeg-cap ${config.ffmpegHasLibx264 ? 'cap-ok' : 'cap-miss'}">${config.ffmpegHasLibx264 ? '✓' : '✗'} H.264 (libx264)</span><span class="ffmpeg-cap ${config.ffmpegHasNvenc ? 'cap-ok' : 'cap-miss'}">${config.ffmpegHasNvenc ? '✓' : '✗'} H.265/HEVC (NVENC)</span>${config.ffmpegHasVaapi ? `<span class="ffmpeg-cap cap-ok">✓ H.265/HEVC (VAAPI)</span>` : `<span class="ffmpeg-cap ${config.ffmpegHasAmf ? 'cap-ok' : 'cap-miss'}">${config.ffmpegHasAmf ? '✓' : '✗'} H.265/HEVC (AMF)</span>`}<span class="ffmpeg-cap ${config.ffmpegHasQsv ? 'cap-ok' : 'cap-miss'}">${config.ffmpegHasQsv ? '✓' : '✗'} H.265/HEVC (QSV)</span><span class="ffmpeg-cap ${config.ffmpegHasLibzimg ? 'cap-ok' : 'cap-miss'}">${config.ffmpegHasLibzimg ? '✓' : '✗'} HDR tone-mapping (libzimg)</span><span class="ffmpeg-cap ${config.ffmpegHasNvdec ? 'cap-ok' : 'cap-miss'}">${config.ffmpegHasNvdec ? '✓' : '✗'} GPU decoding (NVDEC/CUVID)</span></div>${(!config.ffmpegHasLibx264 || !config.ffmpegHasLibzimg) ? '<div class="ffmpeg-build-warn">⚠ ' + this.t('settings.ffmpegUpgradeHint') + '</div>' : ''}</span></div>` : ''}
        </div>`;

        // ── Video Transcoding ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-video"/></svg> ${this.t('settings.videoTranscoding')}</h3>`;
        // GPU detection status
        if (config.detectedGpus && config.detectedGpus.length > 0) {
            html += `<div class="setting-group-label">${this.t('settings.detectedGpus')}</div>`;
            config.detectedGpus.forEach(g => {
                const badge = g.encoderType !== 'none'
                    ? `<span class="settings-badge" style="background:var(--success)">${g.encoderType.toUpperCase()}</span>`
                    : `<span class="settings-badge" style="background:var(--text-muted)">${this.t('settings.noHwEncoder')}</span>`;
                html += `<div class="setting-row"><span class="setting-label">${this.esc(g.vendor)}</span><span class="setting-value">${this.esc(g.name)} ${badge}</span></div>`;
            });
        } else {
            html += `<div class="setting-row"><span class="setting-label">GPU</span><span class="setting-value" style="color:var(--text-muted)">${this.t('settings.noGpuDetected')}</span></div>`;
        }
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.activeEncoder')}</span><span class="setting-value"><span class="settings-badge" style="background:var(--accent)">${this.esc((config.activeEncoder || 'software').toUpperCase())}</span></span></div>`;
        html += `<div class="setting-group-label">${this.t('settings.encoderSettings')}</div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.preferredEncoder')}</span><span class="setting-value">${select('preferredEncoder', config.preferredEncoder, [{v:'auto',l:this.t('settings.autoDetectBest')},{v:'nvenc',l:this.t('settings.nvidiaNvenc')},{v:'qsv',l:this.t('settings.intelQuickSync')},{v:'amf',l:this.t('settings.amdAmf')},{v:'software',l:this.t('settings.softwareCpu')}])} <span class="setting-hint">${this.t('settings.changesRequireRestart')}</span></span></div>`;
        const av1HwLabel = config.av1NvencAvailable ? 'NVIDIA av1_nvenc' : config.av1QsvAvailable ? 'Intel av1_qsv' : config.av1AmfAvailable ? 'AMD av1_amf' : null;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.videoOutputCodec')}</span><span class="setting-value">${select('preferredVideoCodec', config.preferredVideoCodec || 'h264', [{v:'h264',l:this.t('settings.h264Universal')},{v:'av1',l:this.t('settings.av1Better')}])} <span class="setting-hint">${av1HwLabel ? 'HW AV1: ' + av1HwLabel : this.t('settings.av1NoHw')}</span></span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.softwarePreset')}</span><span class="setting-value">${select('videoPreset', config.videoPreset, [{v:'ultrafast',l:this.t('settings.ultrafast')},{v:'veryfast',l:this.t('settings.veryfast')},{v:'fast',l:this.t('settings.fast')},{v:'medium',l:this.t('settings.medium')},{v:'slow',l:this.t('settings.slow')}])}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.qualityCrf')}</span><span class="setting-value">${input('videoCRF', config.videoCRF, 'number')} <span class="setting-hint">${this.t('settings.crfHint')}</span></span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.hwMaxBitrate')}</span><span class="setting-value">${select('videoMaxrate', config.videoMaxrate, [{v:'3M',l:'3 Mbps'},{v:'5M',l:'5 Mbps'},{v:'8M',l:'8 Mbps'},{v:'10M',l:'10 Mbps'},{v:'15M',l:'15 Mbps'}])}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.maxResolution')}</span><span class="setting-value">${select('transcodeMaxHeight', config.transcodeMaxHeight + '', [{v:'0',l:this.t('settings.noLimit')},{v:'1080',l:'1080p'},{v:'720',l:'720p'}])}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.hdrPlaybackMode')}</span><span class="setting-value">${select('hdrPlaybackMode', config.hdrPlaybackMode || 'auto', [{v:'auto',l:this.t('settings.hdrAuto')},{v:'sdr',l:this.t('settings.hdrForceSdr')}])} <span class="setting-hint">${this.t('settings.hdrHint')}</span></span></div>`;
        html += `<div class="setting-group-label">${this.t('settings.performance')}</div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.cpuUsageLimit')}</span><span class="setting-value">${select('ffmpegCPULimit', config.ffmpegCPULimit + '', [{v:'0',l:this.t('settings.unlimited')},{v:'25',l:'25%'},{v:'50',l:'50%'},{v:'60',l:'60%'},{v:'70',l:'70%'},{v:'80',l:'80%'},{v:'90',l:'90%'}])} <span class="setting-hint">${this.t('settings.forSoftwareEncoding')}</span></span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.remuxPriority')}</span><span class="setting-value">${select('remuxPriority', config.remuxPriority, [{v:'normal',l:this.t('settings.normal')},{v:'abovenormal',l:this.t('settings.aboveNormal')},{v:'high',l:this.t('settings.highFastest')}])} <span class="setting-hint">${this.t('settings.fasterRemux')}</span></span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.maxConcurrentTranscodes')}</span><span class="setting-value">${input('maxConcurrentTranscodes', config.maxConcurrentTranscodes, 'number')} <span class="setting-hint">${this.t('settings.recommendedHint')}</span></span></div>`;
        html += `<div class="setting-group-label">${this.t('label.audio')}</div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.audioBitrate')}</span><span class="setting-value">${select('transcodingAudioBitrate', config.transcodingAudioBitrate, [{v:'128k',l:'128 kbps'},{v:'192k',l:'192 kbps'},{v:'256k',l:'256 kbps'},{v:'320k',l:'320 kbps'}])}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.audioChannels')}</span><span class="setting-value">${select('transcodingAudioChannels', config.transcodingAudioChannels + '', [{v:'2',l:this.t('settings.stereo')},{v:'6',l:this.t('settings.surround')}])}</span></div>`;
        html += `<div class="setting-group-label">${this.t('settings.hlsStreaming')}</div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.hlsCache')}</span><span class="setting-value">${toggle('hlsCacheEnabled', config.hlsCacheEnabled)} <span class="setting-hint">${this.t('settings.cacheTranscoded')}</span></span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.maxCacheSize')}</span><span class="setting-value">${input('hlsCacheMaxSizeGB', config.hlsCacheMaxSizeGB, 'number')}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.retention')}</span><span class="setting-value">${input('hlsCacheRetentionDays', config.hlsCacheRetentionDays, 'number')}</span></div>`;
        if (isAdmin) {
            html += `<div style="margin-top:8px"><button class="btn-primary" style="font-size:12px;padding:6px 14px" onclick="App.clearHlsCache()">${this.t('btn.clearHlsCache')}</button> <span id="hls-cache-msg" style="font-size:12px;color:var(--text-muted)"></span></div>`;
        }
        html += `</div>`;

        // ── Logging ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-file-text"/></svg> ${this.t('settings.logging')}</h3>
            <div class="setting-row"><span class="setting-label">${this.t('settings.logLevel')}</span><span class="setting-value">${select('logLevel', config.logLevel, [{v:'Debug',l:this.t('settings.debug')},{v:'Information',l:this.t('settings.information')},{v:'Warning',l:this.t('settings.warning')},{v:'Error',l:this.t('settings.error')}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.logFile')}</span><span class="setting-value" style="font-size:12px;word-break:break-all;color:var(--text-muted)">${this.esc(config.logFile)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.maxLogSize')}</span><span class="setting-value">${input('maxLogSizeMB', config.maxLogSizeMB, 'number')}</span></div>
        </div>`;

        // ── Databases (read-only) ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-disc"/></svg> ${this.t('settings.databases')}</h3>`;
        if (config.databases && config.databases.length > 0) {
            html += `<table class="settings-shares-table"><thead><tr><th>${this.t('table.database')}</th><th>${this.t('table.path')}</th><th>${this.t('table.status')}</th><th>${this.t('label.size')}</th></tr></thead><tbody>`;
            config.databases.forEach(db => {
                const status = db.exists
                    ? `<span class="status-ok">${this.t('nav.online')}</span>`
                    : `<span style="color:var(--warning)">${this.t('settings.notFound')}</span>`;
                const sizeHtml = db.sizeBytes > 0
                    ? `<span style="color:var(--text-muted)">${this.formatSize(db.sizeBytes)}</span>`
                    : '';
                html += `<tr>
                    <td style="font-weight:500">${this.esc(db.name)}</td>
                    <td class="settings-share-path">${this.esc(db.path)}</td>
                    <td>${status}</td>
                    <td>${sizeHtml}</td>
                </tr>`;
            });
            html += '</tbody></table>';
        }
        html += '</div>';

        // ── System Power (admin only) ──
        if (isAdmin) {
            html += `<div class="settings-section">
                <h3><svg class="settings-icon"><use href="#icon-power"/></svg> System Power</h3>
                <p style="color:var(--text-secondary);font-size:13px;margin-bottom:16px">Shut down or restart the host machine running the NexusM server.</p>
                <div style="display:flex;gap:12px;flex-wrap:wrap">
                    <button class="btn-primary" style="background:#e67e22;display:inline-flex;align-items:center;gap:8px" onclick="App.rebootHost()">
                        <svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-reboot"/></svg>
                        Reboot
                    </button>
                    <button class="btn-primary" style="background:#e74c3c;display:inline-flex;align-items:center;gap:8px" onclick="App.shutdownHost()">
                        <svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-power"/></svg>
                        Shut Down
                    </button>
                </div>
                <div id="system-power-msg" style="margin-top:12px;font-size:13px"></div>
            </div>`;
        }

        // ── Server Logs (admin only) ──
        if (isAdmin) {
            html += `<div class="settings-section">
                <h3><svg class="settings-icon"><use href="#icon-file-text"/></svg> ${this.t('settings.serverLogs')}</h3>
                <div class="setting-row"><span class="setting-label">${this.t('settings.logFile')}</span><span class="setting-value"><code style="font-size:11px;word-break:break-all;user-select:all">${this.esc(config.logFile || '')}</code></span></div>
                <div style="margin-top:10px;display:flex;align-items:center;gap:8px">
                    <button class="btn-secondary" id="btn-load-logs" onclick="App.loadServerLogs()">${this.t('btn.loadLogs')}</button>
                    <label style="display:flex;align-items:center;gap:6px;font-size:13px;cursor:pointer"><input type="checkbox" id="log-auto-refresh" onchange="App.toggleLogAutoRefresh(this.checked)"> ${this.t('btn.autoRefreshLogs')}</label>
                </div>
                <div id="log-viewer" class="log-viewer" style="display:none"></div>
            </div>`;
        }

        // ── Floating Save Button (admin only) ──
        if (isAdmin) {
            html += `<div class="settings-save-floating" id="settings-save-floating">
                <button class="settings-save-btn" onclick="App.saveSettings()">
                    <svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;vertical-align:-2px"><use href="#icon-download"/></svg>
                    ${this.t('btn.saveSettings')}
                </button>
                <span class="settings-save-msg" id="settings-save-msg"></span>
            </div>`;
        }

        el.innerHTML = html;

        // Start live resource metrics polling (charts refresh every 15s in-place)
        this._startMetricsPolling();

        // Load share credentials for each folder type
        this.loadShareCredentials();

        // Populate template cards from server
        this._loadTemplateGrid();
    },

    _sharesCache: null,

    async loadShareCredentials() {
        const shares = await this.api('shares');
        this._sharesCache = shares || [];
        const types = ['music', 'movies', 'tvshows', 'musicvideos', 'pictures', 'ebooks'];
        types.forEach(type => {
            const container = document.getElementById(`share-creds-${type}`);
            if (!container) return;
            const share = this._sharesCache.find(s => s.folderType === type);
            if (share) {
                // Show a small inline status badge next to the lock button
                const btn = container.previousElementSibling?.querySelector('button');
                if (btn) {
                    const badge = share.isMounted ? '&#128275;' : '&#128274;';
                    btn.innerHTML = badge;
                    btn.title = share.isMounted ? 'Mounted - Click to manage credentials' : 'Has credentials - Click to manage';
                }
            }
        });
    },

    async toggleShareCredentials(folderType) {
        const container = document.getElementById(`share-creds-${folderType}`);
        if (!container) return;

        // Toggle visibility
        if (container.style.display !== 'none' && container.innerHTML) {
            container.style.display = 'none';
            container.innerHTML = '';
            return;
        }

        // Load existing credentials for this folder type
        if (!this._sharesCache) {
            const shares = await this.api('shares');
            this._sharesCache = shares || [];
        }
        const existing = this._sharesCache.find(s => s.folderType === folderType);

        const statusBadge = existing
            ? (existing.isMounted
                ? '<span style="display:inline-block;background:var(--success,#27ae60);color:#fff;padding:1px 8px;border-radius:10px;font-size:10px;font-weight:600">MOUNTED</span>'
                : '<span style="display:inline-block;background:var(--text-muted,#666);color:#fff;padding:1px 8px;border-radius:10px;font-size:10px;font-weight:600">NOT MOUNTED</span>')
            : '';

        container.style.display = 'block';
        container.innerHTML = `<div style="margin:4px 0 12px 0;padding:12px;background:rgba(255,255,255,.02);border:1px solid var(--border,rgba(255,255,255,.08));border-radius:8px">
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:10px">
                <strong style="font-size:12px">${this.t('settings.networkCredentials', 'Network Credentials')}</strong>
                ${statusBadge}
                ${existing && existing.lastError ? '<span style="font-size:11px;color:var(--danger,#e74c3c)">' + this.esc(existing.lastError) + '</span>' : ''}
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px 12px;font-size:13px">
                <div><label style="font-size:11px;color:var(--text-secondary,#888)">${this.t('label.username', 'Username')}</label><input id="sc-user-${folderType}" class="setting-input" style="width:100%" value="${existing ? this.esc(existing.username) : ''}" autocomplete="off"></div>
                <div><label style="font-size:11px;color:var(--text-secondary,#888)">${this.t('label.password', 'Password')}</label><input id="sc-pass-${folderType}" type="password" class="setting-input" style="width:100%" placeholder="${existing && existing.hasPassword ? '(saved - leave empty to keep)' : ''}" autocomplete="new-password"></div>
                <div><label style="font-size:11px;color:var(--text-secondary,#888)">${this.t('label.domain', 'Domain')}</label><input id="sc-domain-${folderType}" class="setting-input" style="width:100%" value="${existing ? this.esc(existing.domain || '') : ''}" placeholder="(optional)"></div>
                <div><label style="font-size:11px;color:var(--text-secondary,#888)">${this.t('label.mountPoint', 'Mount Point')}</label><input id="sc-mount-${folderType}" class="setting-input" style="width:100%" value="${existing ? this.esc(existing.mountPoint || '') : ''}" placeholder="(auto)"></div>
                <div style="grid-column:1/-1"><label style="font-size:11px;color:var(--text-secondary,#888)">${this.t('label.mountOptions', 'Mount Options')}</label><input id="sc-opts-${folderType}" class="setting-input" style="width:100%" value="${existing ? this.esc(existing.mountOptions || '') : ''}" placeholder="vers=3.0"></div>
            </div>
            <div style="display:flex;gap:6px;margin-top:10px;align-items:center">
                <button class="btn-action" style="font-size:12px;padding:4px 12px;background:var(--accent,#3498db);color:#fff" onclick="App.saveShareCredentials('${folderType}')">${this.t('btn.save', 'Save')}</button>
                <button class="btn-action" style="font-size:12px;padding:4px 12px" onclick="App.testShareCredentials('${folderType}')">${this.t('btn.testConnection', 'Test')}</button>
                ${existing ? `<button class="btn-action" style="font-size:12px;padding:4px 12px" onclick="App.toggleMountShare(${existing.id}, ${existing.isMounted})">${existing.isMounted ? this.t('btn.unmount', 'Unmount') : this.t('btn.mount', 'Mount')}</button>` : ''}
                ${existing ? `<button class="btn-action" style="font-size:12px;padding:4px 12px;color:var(--danger,#e74c3c)" onclick="App.deleteShare(${existing.id})">${this.t('btn.removeCredentials', 'Remove')}</button>` : ''}
                <div id="sc-status-${folderType}" style="font-size:11px;margin-left:8px"></div>
            </div>
        </div>`;
    },

    async saveShareCredentials(folderType) {
        const username = document.getElementById(`sc-user-${folderType}`)?.value?.trim();
        const password = document.getElementById(`sc-pass-${folderType}`)?.value;
        const domain = document.getElementById(`sc-domain-${folderType}`)?.value?.trim() || '';
        const mountPoint = document.getElementById(`sc-mount-${folderType}`)?.value?.trim() || '';
        const mountOptions = document.getElementById(`sc-opts-${folderType}`)?.value?.trim() || '';
        const statusEl = document.getElementById(`sc-status-${folderType}`);

        // Get the share path from the corresponding folder input
        const keyMap = { music: 'musicFolders', movies: 'moviesFolders', tvshows: 'tvShowsFolders', musicvideos: 'musicVideosFolders', pictures: 'picturesFolders', ebooks: 'ebooksFolders', audiobooks: 'audioBooksFolders', anime: 'animeFolders' };
        const sharePath = document.getElementById(`cfg-${keyMap[folderType]}`)?.value?.trim();

        if (!sharePath) { if (statusEl) { statusEl.style.color = 'var(--danger,#e74c3c)'; statusEl.textContent = 'Enter a folder path first'; } return; }
        if (!username) { if (statusEl) { statusEl.style.color = 'var(--danger,#e74c3c)'; statusEl.textContent = 'Username is required'; } return; }

        const existing = (this._sharesCache || []).find(s => s.folderType === folderType);

        let res;
        if (existing) {
            const payload = { sharePath, username, domain, mountPoint, mountOptions, folderType };
            if (password) payload.password = password;
            res = await this.apiPut(`shares/${existing.id}`, payload);
        } else {
            if (!password) { if (statusEl) { statusEl.style.color = 'var(--danger,#e74c3c)'; statusEl.textContent = 'Password is required for new credentials'; } return; }
            res = await this.apiPost('shares', { sharePath, username, password, domain, mountPoint, mountOptions, shareType: 'smb', folderType });
        }

        if (res && res.success) {
            if (statusEl) { statusEl.style.color = 'var(--success,#27ae60)'; statusEl.textContent = 'Credentials saved, mounting...'; }
            // Auto-mount after saving credentials
            const shareId = res.id || (existing && existing.id);
            if (shareId) await this.apiPost(`shares/${shareId}/mount`, {});
            this._sharesCache = null;
            await this.loadShareCredentials();
            // Re-expand to show updated state
            const container = document.getElementById(`share-creds-${folderType}`);
            if (container) { container.style.display = 'none'; container.innerHTML = ''; }
            setTimeout(() => this.toggleShareCredentials(folderType), 200);
        } else if (statusEl) {
            statusEl.style.color = 'var(--danger,#e74c3c)';
            statusEl.textContent = (res && res.message) || 'Failed to save';
        }
    },

    async testShareCredentials(folderType) {
        const statusEl = document.getElementById(`sc-status-${folderType}`);
        if (statusEl) { statusEl.style.color = 'var(--text-secondary,#888)'; statusEl.textContent = 'Testing...'; }

        const keyMap = { music: 'musicFolders', movies: 'moviesFolders', tvshows: 'tvShowsFolders', musicvideos: 'musicVideosFolders', pictures: 'picturesFolders', ebooks: 'ebooksFolders', audiobooks: 'audioBooksFolders', anime: 'animeFolders' };
        const sharePath = document.getElementById(`cfg-${keyMap[folderType]}`)?.value?.trim() || '';
        const password = document.getElementById(`sc-pass-${folderType}`)?.value || '';

        const res = await this.apiPost('shares/test', {
            sharePath,
            username: document.getElementById(`sc-user-${folderType}`)?.value?.trim() || '',
            password,
            domain: document.getElementById(`sc-domain-${folderType}`)?.value?.trim() || ''
        });
        if (statusEl && res) {
            statusEl.style.color = res.success ? 'var(--success,#27ae60)' : 'var(--danger,#e74c3c)';
            statusEl.textContent = res.message || (res.success ? 'Connection successful' : 'Connection failed');
        }
    },

    async toggleMountShare(id, isMounted) {
        const endpoint = isMounted ? `shares/${id}/unmount` : `shares/${id}/mount`;
        await this.apiPost(endpoint, {});
        this._sharesCache = null;
        await this.loadShareCredentials();
    },

    async deleteShare(id) {
        if (!confirm(this.t('confirm.deleteShare', 'Are you sure you want to remove these credentials?'))) return;
        await this.apiDelete(`shares/${id}`);
        this._sharesCache = null;
        await this.loadShareCredentials();
        // Collapse all credential panels
        ['music', 'movies', 'tvshows', 'musicvideos', 'pictures', 'ebooks'].forEach(t => {
            const c = document.getElementById(`share-creds-${t}`);
            if (c) { c.style.display = 'none'; c.innerHTML = ''; }
        });
    },

    async mountAllShares() {
        const res = await this.apiPost('shares/mount-all', {});
        if (res) {
            this._sharesCache = null;
            await this.loadShareCredentials();
        }
    },

    // ── Trakt.tv connect / disconnect / poll ──────────────────────────────

    async traktConnect() {
        const statusCell = document.getElementById('trakt-status-cell');
        const pinPanel   = document.getElementById('trakt-pin-panel');
        const pinCode    = document.getElementById('trakt-pin-code');
        const pinStatus  = document.getElementById('trakt-pin-status');

        if (statusCell) statusCell.innerHTML = `<span style="color:var(--text-secondary)">${this.t('settings.traktConnecting')}</span>`;

        const data = await this.apiPost('trakt/connect', {});
        if (!data || data.error) {
            if (statusCell) statusCell.innerHTML = `<span style="color:var(--error)">${data?.error || 'Failed to start auth'}</span>`;
            return;
        }

        if (pinPanel) pinPanel.style.display = 'block';
        if (pinCode)  pinCode.textContent = data.userCode;
        if (pinStatus) pinStatus.textContent = this.t('settings.traktConnecting');

        // Poll every `interval` seconds until authorized or expired
        const interval = (data.interval || 5) * 1000;
        this._traktPollTimer = setInterval(async () => {
            const poll = await this.api('trakt/poll');
            if (!poll) return;

            if (poll.status === 'authorized') {
                clearInterval(this._traktPollTimer);
                if (pinPanel) pinPanel.style.display = 'none';
                if (statusCell) statusCell.innerHTML =
                    `<span class="trakt-connected-badge">&#9679; ${this.t('settings.traktConnectedAs')} <strong>${this.esc(poll.traktUsername)}</strong></span>
                     <button class="btn-secondary trakt-action-btn" onclick="App.traktDisconnect()">${this.t('btn.traktDisconnect')}</button>`;
                // Update cached config so scrobbling works without page reload
                if (this._initCfg) { this._initCfg.traktConnected = true; this._initCfg.traktUsername = poll.traktUsername; }
            } else if (poll.status === 'expired' || poll.status === 'error') {
                clearInterval(this._traktPollTimer);
                if (pinPanel) pinPanel.style.display = 'none';
                if (statusCell) statusCell.innerHTML =
                    `<span style="color:var(--error)">${poll.status === 'expired' ? 'Code expired — try again' : 'Auth error — try again'}</span>
                     <button class="btn-secondary trakt-action-btn" onclick="App.traktConnect()">${this.t('btn.traktConnect')}</button>`;
            }
        }, interval);
    },

    async traktDisconnect() {
        await this.apiDelete('trakt/connect');
        const statusCell = document.getElementById('trakt-status-cell');
        if (statusCell) statusCell.innerHTML =
            `<span style="color:var(--text-secondary)">&#9675; ${this.t('settings.traktNotConnected')}</span>
             <button class="btn-secondary trakt-action-btn" onclick="App.traktConnect()">${this.t('btn.traktConnect')}</button>`;
        if (this._initCfg) { this._initCfg.traktConnected = false; this._initCfg.traktUsername = ''; }
    },

    async traktSubmitPin() {
        const pinInput   = document.getElementById('trakt-auth-pin');
        const pinStatus  = document.getElementById('trakt-pin-status');
        const pinPanel   = document.getElementById('trakt-pin-panel');
        const statusCell = document.getElementById('trakt-status-cell');
        const pin = pinInput?.value?.trim();
        if (!pin) { if (pinStatus) pinStatus.textContent = 'Please enter the PIN from Trakt.'; return; }
        if (pinStatus) pinStatus.textContent = 'Authorizing…';
        if (this._traktPollTimer) { clearInterval(this._traktPollTimer); this._traktPollTimer = null; }
        const data = await this.apiPost('trakt/pin', { pin });
        if (!data || data.error) {
            if (pinStatus) pinStatus.style.color = 'var(--error)';
            if (pinStatus) pinStatus.textContent = data?.error || 'PIN exchange failed — try again.';
            return;
        }
        if (pinPanel) pinPanel.style.display = 'none';
        if (statusCell) statusCell.innerHTML =
            `<span class="trakt-connected-badge">&#9679; ${this.t('settings.traktConnectedAs')} <strong>${this.esc(data.traktUsername)}</strong></span>
             <button class="btn-secondary trakt-action-btn" onclick="App.traktDisconnect()">${this.t('btn.traktDisconnect')}</button>`;
        if (this._initCfg) { this._initCfg.traktConnected = true; this._initCfg.traktUsername = data.traktUsername; }
    },

    // ─────────────────────────────────────────────────────────────────────

    async saveSettings() {
        const v = id => { const e = document.getElementById('cfg-' + id); return e ? e.value : ''; };
        const b = id => { const e = document.getElementById('cfg-' + id); return e ? e.checked : false; };
        const n = id => { const e = document.getElementById('cfg-' + id); return e ? parseInt(e.value) || 0 : 0; };

        const payload = {
            // Server
            serverHost: v('serverHost'),
            serverPort: n('serverPort'),
            httpsEnabled: b('httpsEnabled'),
            httpsPort: n('httpsPort'),
            httpsRedirectHttp: b('httpsRedirectHttp'),
            httpsCertPassword: v('httpsCertPassword'),
            workerThreads: n('workerThreads'),
            requestTimeout: n('requestTimeout'),
            sessionTimeout: n('sessionTimeout'),
            showConsole: b('showConsole'),
            openBrowser: b('openBrowser'),
            runOnStartup: b('runOnStartup'),
            // Trakt
            traktClientId:       v('traktClientId'),
            traktClientSecret:   v('traktClientSecret'),
            traktScrobbleEnabled: b('traktScrobbleEnabled'),
            // DLNA
            dlnaEnabled: b('dlnaEnabled'),
            dlnaFriendlyName: v('dlnaFriendlyName'),
            // Security
            securityByPin: b('securityByPin'),
            defaultAdminUser: v('defaultAdminUser'),
            ipWhitelist: v('ipWhitelist'),
            // UI
            theme: v('theme'),
            uiTemplate: v('uiTemplate'),
            defaultView: v('defaultView'),
            language: v('language'),
            // Menu
            showMusic: b('showMusic'),
            showPictures: b('showPictures'),
            showMoviesTV: b('showMoviesTV'),
            showMovies: b('showMovies'),
            showTvShows: b('showTvShows'),
            showMusicVideos: b('showMusicVideos'),
            showRadio: b('showRadio'),
            showInternetTV: b('showInternetTV'),
            showEBooks: b('showEBooks'),
            showAudioBooks: b('showAudioBooks'),
            showActors: b('showActors'),
            showPodcasts: b('showPodcasts'),
            showAnime: b('showAnime'),
            goBigDefault: b('goBigDefault'),
            // Library
            musicFolders: v('musicFolders'),
            moviesTVFolders: v('moviesTVFolders'),
            moviesFolders: v('moviesFolders'),
            tvShowsFolders: v('tvShowsFolders'),
            musicVideosFolders: v('musicVideosFolders'),
            picturesFolders: v('picturesFolders'),
            ebooksFolders: v('ebooksFolders'),
            audioBooksFolders: v('audioBooksFolders'),
            animeFolders: v('animeFolders'),
            autoScanOnStartup: b('autoScanOnStartup'),
            autoScanInterval: n('autoScanInterval'),
            scanThreads: n('scanThreads'),
            // Extensions
            audioExtensions: v('audioExtensions'),
            videoExtensions: v('videoExtensions'),
            musicVideoExtensions: v('musicVideoExtensions'),
            imageExtensions: v('imageExtensions'),
            ebookExtensions: v('ebookExtensions'),
            audioBooksExtensions: v('audioBooksExtensions'),
            // Metadata
            metadataProvider: v('metadataProvider'),
            tmdbApiKey: v('tmdbApiKey'),
            watchmodeApiKey: v('watchmodeApiKey'),
            fetchMetadataOnScan: b('fetchMetadataOnScan'),
            fetchCastPhotos: b('fetchCastPhotos'),
            // Subtitles
            openSubtitlesApiKey: v('openSubtitlesApiKey'),
            openSubtitlesUsername: v('openSubtitlesUsername'),
            openSubtitlesPassword: v('openSubtitlesPassword'),
            subDlApiKey: v('subDlApiKey'),
            // Playback
            transcodingEnabled: b('transcodingEnabled'),
            introSkipperEnabled: b('introSkipperEnabled'),
            transcodeFormat: v('transcodeFormat'),
            transcodeBitrate: v('transcodeBitrate'),
            ffmpegPath: v('ffmpegPath'),
            // Video Transcoding
            preferredEncoder: v('preferredEncoder'),
            preferredVideoCodec: v('preferredVideoCodec'),
            videoPreset: v('videoPreset'),
            videoCRF: n('videoCRF'),
            videoMaxrate: v('videoMaxrate'),
            transcodeMaxHeight: n('transcodeMaxHeight'),
            hdrPlaybackMode: v('hdrPlaybackMode'),
            ffmpegCPULimit: n('ffmpegCPULimit'),
            remuxPriority: v('remuxPriority'),
            maxConcurrentTranscodes: n('maxConcurrentTranscodes'),
            transcodingAudioBitrate: v('transcodingAudioBitrate'),
            transcodingAudioChannels: n('transcodingAudioChannels'),
            hlsCacheEnabled: b('hlsCacheEnabled'),
            hlsCacheMaxSizeGB: n('hlsCacheMaxSizeGB'),
            hlsCacheRetentionDays: n('hlsCacheRetentionDays'),
            // Logging
            logLevel: v('logLevel'),
            maxLogSizeMB: n('maxLogSizeMB')
        };

        const msg = document.getElementById('settings-save-msg');
        if (msg) { msg.style.color = 'var(--text-muted)'; msg.textContent = this.t('status.saving'); }

        const res = await this.apiPost('config/save', payload);
        if (res && res.success) {
            // Apply theme + template changes live
            const newTheme = payload.theme || 'dark';
            this.applyTheme(newTheme);
            const newTemplate = payload.uiTemplate || '';
            if (newTemplate !== (this._activeTemplate || '')) {
                this.applyTemplate(newTemplate);
            }
            // Apply menu visibility dynamically
            this.applyMenuVisibility();
            // Apply language change dynamically
            const newLang = payload.language;
            if (newLang && newLang !== this._langCode) {
                await this.loadLanguage(newLang);
                this.navigate('settings'); // re-render settings page with new language
                return;
            }
            if (msg) { msg.style.color = 'var(--success)'; msg.textContent = this.t('status.saved'); }
            this.showRestartModal();
        } else {
            if (msg) { msg.style.color = 'var(--danger)'; msg.textContent = this.t('status.failedToSave'); }
        }
    },

    showRestartModal() {
        let overlay = document.getElementById('nexus-restart-modal');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'nexus-restart-modal';
            overlay.className = 'nexus-modal-overlay';
            overlay.innerHTML = `
                <div class="nexus-modal">
                    <div class="nexus-modal-icon">
                        <svg width="38" height="38" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
                            <use href="#icon-settings"/>
                        </svg>
                    </div>
                    <div class="nexus-modal-title">${this.t('modal.settingsSaved')}</div>
                    <div class="nexus-modal-body">${this.t('modal.restartRequired')}</div>
                    <button class="nexus-modal-btn" onclick="App.dismissRestartModal()">${this.t('btn.ok')}</button>
                </div>`;
            document.body.appendChild(overlay);
        }
        overlay.style.display = 'flex';
    },

    dismissRestartModal() {
        const overlay = document.getElementById('nexus-restart-modal');
        if (overlay) overlay.style.display = 'none';
        window.location.reload();
    },

    async fetchAllVideoMetadata(refetchAll) {
        // Status element exists on Settings page; on Rescan page fall back to video-scan-status
        const msg = document.getElementById('metadata-fetch-status') || document.getElementById('video-scan-status');
        const setMsg = (text, color) => {
            if (!msg) return;
            if (msg.id === 'video-scan-status') {
                msg.innerHTML = `<div class="scan-bar"><div class="scan-text">${text}</div></div>`;
            } else {
                msg.style.color = color || 'var(--text-muted)';
                msg.textContent = text;
            }
        };
        setMsg(refetchAll ? this.t('status.resettingRefetching') : this.t('status.starting'));
        try {
            const url = refetchAll ? 'videos/fetch-metadata?refetchAll=true' : 'videos/fetch-metadata';
            await this.apiPost(url, {});
            this._pollMetadataFetchStatus(setMsg);
        } catch(e) {
            setMsg(this.t('status.failedToStartMetadata'), 'var(--danger)');
        }
    },

    _pollMetadataFetchStatus(setMsg) {
        if (this._metadataPollTimer) clearInterval(this._metadataPollTimer);
        // Grace period: allow up to 5 "not fetching" responses before concluding done
        // (Task.Run may not have started yet on the first few polls)
        let idleCount = 0;
        this._metadataPollTimer = setInterval(async () => {
            try {
                const s = await this.api('videos/fetch-metadata/status');
                if (!s) return;
                if (s.isFetching) {
                    idleCount = 0;
                    const pct = s.total > 0 ? ` (${s.processed}/${s.total})` : '';
                    setMsg(`Fetching metadata\u2026${pct}`);
                } else {
                    idleCount++;
                    if (idleCount >= 5) {
                        clearInterval(this._metadataPollTimer);
                        this._metadataPollTimer = null;
                        setMsg(s.message || 'Done.', 'var(--success)');
                    }
                }
            } catch(e) {
                clearInterval(this._metadataPollTimer);
                this._metadataPollTimer = null;
            }
        }, 1500);
    },

    // ─── HTTPS cert helpers ───────────────────────────────────────────

    async downloadCert() {
        try {
            const resp = await fetch('/api/https/download-cert');
            if (!resp.ok) {
                const err = await resp.json().catch(() => ({}));
                alert(err.error || 'Failed to download certificate');
                return;
            }
            const blob = await resp.blob();
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'nexusm.crt';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        } catch (e) {
            alert('Failed to download certificate: ' + e.message);
        }
    },

    async regenerateCert() {
        if (!confirm(this.t('settings.httpsRegenConfirm'))) return;
        const btn = document.getElementById('btn-regen-cert');
        const origLabel = btn ? btn.innerHTML : '';
        if (btn) { btn.disabled = true; btn.textContent = '...'; }
        try {
            const res = await this.apiPost('https/regenerate-cert', {});
            if (res && res.message) {
                alert(res.message + (res.expiry ? `\nNew expiry: ${res.expiry}` : ''));
                this.navigate('settings');
            } else if (res && res.error) {
                alert(res.error);
            } else {
                alert('No response from server — check server logs.');
            }
        } catch (e) {
            alert('Failed to regenerate certificate: ' + e.message);
        } finally {
            if (btn) { btn.disabled = false; btn.innerHTML = origLabel; }
        }
    },

    _logAutoRefreshTimer: null,

    async loadServerLogs() {
        const btn = document.getElementById('btn-load-logs');
        const viewer = document.getElementById('log-viewer');
        if (!viewer) return;
        if (btn) { btn.disabled = true; btn.textContent = '...'; }
        try {
            const res = await this.api('config/logs?lines=300');
            viewer.style.display = '';
            if (!res || res.error) {
                viewer.innerHTML = `<span class="log-wrn">${this.esc(res?.error || 'Could not load logs')}</span>`;
            } else {
                const colorLine = line => {
                    const e = this.esc(line);
                    if (line.includes('[ERR]') || line.includes('[FTL]')) return `<span class="log-err">${e}</span>`;
                    if (line.includes('[WRN]')) return `<span class="log-wrn">${e}</span>`;
                    if (line.includes('FFmpeg command') || line.includes('NVDEC') || line.includes('NVDEC not available') || line.includes('HDR source') || line.includes('cuvidActive') || line.includes('nvenc') || line.includes('h264_nvenc'))
                        return `<span class="log-gpu">${e}</span>`;
                    return e;
                };
                viewer.innerHTML = res.lines.map(colorLine).join('\n');
                viewer.scrollTop = viewer.scrollHeight;
            }
            if (btn) { btn.disabled = false; btn.textContent = this.t('btn.refreshLogs'); }
        } catch (e) {
            viewer.style.display = '';
            viewer.innerHTML = `<span class="log-err">Error: ${this.esc(String(e))}</span>`;
            if (btn) { btn.disabled = false; btn.textContent = this.t('btn.loadLogs'); }
        }
    },

    toggleLogAutoRefresh(on) {
        clearInterval(this._logAutoRefreshTimer);
        this._logAutoRefreshTimer = null;
        if (on) {
            this.loadServerLogs();
            this._logAutoRefreshTimer = setInterval(() => this.loadServerLogs(), 5000);
        }
    },

    async clearHlsCache() {
        const msg = document.getElementById('hls-cache-msg');
        if (msg) msg.textContent = 'Clearing...';
        try {
            const res = await this.apiPost('transcode/clear-hls-cache', {});
            if (res && res.count !== undefined) {
                if (msg) { msg.style.color = 'var(--success)'; msg.textContent = `Cleared ${res.count} entries (${this.formatSize(res.freed)} freed)`; }
            } else {
                if (msg) { msg.style.color = 'var(--danger)'; msg.textContent = 'Failed to clear cache'; }
            }
            setTimeout(() => { if (msg) msg.textContent = ''; }, 5000);
        } catch (e) {
            if (msg) { msg.style.color = 'var(--danger)'; msg.textContent = 'Error clearing cache'; }
        }
    },

    // ─── System Power Actions ────────────────────────────────
    async shutdownHost() {
        if (!confirm('Are you sure you want to SHUT DOWN the host machine?\n\nThe NexusM server and all services on this machine will stop.')) return;
        if (!confirm('This will shut down the entire system. Last chance to cancel.\n\nProceed with shutdown?')) return;
        const msg = document.getElementById('system-power-msg');
        if (msg) { msg.style.color = 'var(--warning)'; msg.textContent = 'Sending shutdown command...'; }
        try {
            const res = await this.apiPost('system/shutdown');
            if (res && res.success) {
                if (msg) { msg.style.color = 'var(--danger)'; msg.innerHTML = '<strong>Shutdown initiated.</strong> The system will shut down in a few seconds. You will lose connection to this server.'; }
            } else {
                if (msg) { msg.style.color = 'var(--danger)'; msg.textContent = 'Failed to initiate shutdown.'; }
            }
        } catch (e) {
            if (msg) { msg.style.color = 'var(--danger)'; msg.textContent = 'Error: ' + e.message; }
        }
    },

    async rebootHost() {
        if (!confirm('Are you sure you want to REBOOT the host machine?\n\nThe NexusM server will restart along with the system.')) return;
        const msg = document.getElementById('system-power-msg');
        if (msg) { msg.style.color = 'var(--warning)'; msg.textContent = 'Sending reboot command...'; }
        try {
            const res = await this.apiPost('system/reboot');
            if (res && res.success) {
                if (msg) { msg.style.color = '#e67e22'; msg.innerHTML = '<strong>Reboot initiated.</strong> The system will restart in a few seconds. Please wait and then refresh this page.'; }
            } else {
                if (msg) { msg.style.color = 'var(--danger)'; msg.textContent = 'Failed to initiate reboot.'; }
            }
        } catch (e) {
            if (msg) { msg.style.color = 'var(--danger)'; msg.textContent = 'Error: ' + e.message; }
        }
    },

    // ─── User Management Actions ─────────────────────────────
    showAddUser() {
        const area = document.getElementById('user-mgmt-form-area');
        if (!area) return;
        area.innerHTML = `<div class="user-mgmt-form">
            <h4>Add New User</h4>
            <div class="user-mgmt-fields">
                <div class="user-mgmt-field">
                    <label>Username</label>
                    <input type="text" id="new-username" placeholder="username" autocomplete="off">
                </div>
                <div class="user-mgmt-field">
                    <label>Display Name</label>
                    <input type="text" id="new-displayname" placeholder="Display Name" autocomplete="off">
                </div>
                <div class="user-mgmt-field">
                    <label>PIN (6 digits)</label>
                    <input type="password" id="new-pin" maxlength="6" placeholder="000000" inputmode="numeric" autocomplete="new-password">
                </div>
                <div class="user-mgmt-field">
                    <label>Role</label>
                    <select id="new-role" onchange="App._onNewRoleChange()">
                        <option value="guest">Guest</option>
                        <option value="admin">Admin</option>
                        <option value="child">${this.t('settings.child')}</option>
                    </select>
                </div>
            </div>
            <div id="new-child-settings-area"></div>
            <div class="user-mgmt-form-actions">
                <button class="user-mgmt-btn user-mgmt-btn-save" onclick="App.submitAddUser()">Create User</button>
                <button class="user-mgmt-btn" onclick="document.getElementById('user-mgmt-form-area').innerHTML=''">Cancel</button>
            </div>
            <div id="user-mgmt-msg"></div>
        </div>`;
    },

    async submitAddUser() {
        const username = document.getElementById('new-username')?.value?.trim();
        const displayName = document.getElementById('new-displayname')?.value?.trim();
        const pin = document.getElementById('new-pin')?.value;
        const userType = document.getElementById('new-role')?.value;
        const msgEl = document.getElementById('user-mgmt-msg');

        if (!username) { msgEl.innerHTML = '<span class="user-mgmt-error">Username is required</span>'; return; }
        if (!pin || !/^\d{6}$/.test(pin)) { msgEl.innerHTML = '<span class="user-mgmt-error">PIN must be exactly 6 digits</span>'; return; }

        let childSettings = null;
        if (userType === 'child' || userType === 'guest') {
            const sectionKeys = ['showMusic','showPictures','showMovies','showTvShows','showMusicVideos',
                                 'showRadio','showInternetTV','showEBooks','showAudioBooks','showPodcasts','showAnime'];
            const cs = {};
            sectionKeys.forEach(key => {
                const el = document.getElementById(`cs-new-${key}`);
                cs[key] = el ? el.checked : true;
            });
            if (userType === 'child') {
                ['allowPG13','allowTV14'].forEach(key => {
                    const el = document.getElementById(`cs-new-${key}`);
                    cs[key] = el ? el.checked : false;
                });
                cs.viewingHoursEnabled = document.getElementById('cs-new-viewingHoursEnabled')?.checked ?? false;
                cs.viewingHoursStart   = document.getElementById('cs-new-viewingHoursStart')?.value   ?? '08:00';
                cs.viewingHoursEnd     = document.getElementById('cs-new-viewingHoursEnd')?.value     ?? '22:00';
            }
            childSettings = JSON.stringify(cs);
        }

        const result = await this.apiPost('auth/users', { username, pin, userType, displayName: displayName || null, childSettings });
        if (result && result.message) {
            this.navigate('settings');
        } else if (result && result.error) {
            msgEl.innerHTML = `<span class="user-mgmt-error">${this.esc(result.error)}</span>`;
        }
    },

    showEditUser(id, username, displayName, role) {
        const row = document.getElementById(`user-row-${id}`);
        if (!row) return;
        // Read childSettings from the in-memory user list
        const userEntry = (this._adminUsers || []).find(u => u.id === id);
        const rawCs = userEntry ? userEntry.childSettings : row.dataset.childSettings;
        const savedCs = rawCs ? (() => { try { return JSON.parse(rawCs); } catch(e) { return null; } })() : null;
        const gd = { showMusic:true, showPictures:true, showMovies:true, showTvShows:true, showMusicVideos:true,
            showRadio:true, showInternetTV:true, showEBooks:true, showAudioBooks:true, showPodcasts:true, showAnime:true };
        const _csDefaults = role === 'child' ? { ...gd, allowPG13:false, allowTV14:false } : { ...gd };
        this._currentEditChildSettings = Object.assign({}, _csDefaults, savedCs || {});
        row.innerHTML = `
            <td><input type="text" class="user-mgmt-inline-input" id="edit-displayname-${id}" value="${this.esc(displayName)}" placeholder="Display Name"></td>
            <td><select class="user-mgmt-inline-input" id="edit-role-${id}" onchange="App._onEditRoleChange(${id})">
                <option value="guest" ${role === 'guest' ? 'selected' : ''}>Guest</option>
                <option value="admin" ${role === 'admin' ? 'selected' : ''}>Admin</option>
                <option value="child" ${role === 'child' ? 'selected' : ''}>${this.t('settings.child')}</option>
            </select></td>
            <td><input type="password" class="user-mgmt-inline-input" id="edit-pin-${id}" maxlength="6" placeholder="New PIN (optional)" inputmode="numeric"></td>
            <td colspan="2" class="user-mgmt-actions">
                <button class="user-mgmt-btn user-mgmt-btn-save" onclick="App.submitEditUser(${id})">Save</button>
                <button class="user-mgmt-btn" onclick="App.cancelEditUser(${id})">Cancel</button>
            </td>`;
        // Remove any stale child settings row from a previous edit session
        document.getElementById(`child-settings-row-${id}`)?.remove();
        // Insert child settings as a second <tr> directly below the edit row — same visual group
        if (role === 'child' || role === 'guest') {
            let panelHtml = '';
            if (role === 'child') panelHtml = this._renderChildSettingsHtml(id, savedCs);
            else panelHtml = this._renderGuestSettingsHtml(id, savedCs);
            const settingsRow = document.createElement('tr');
            settingsRow.id = `child-settings-row-${id}`;
            settingsRow.innerHTML = `<td colspan="5" class="child-settings-inline-cell">${panelHtml}</td>`;
            row.insertAdjacentElement('afterend', settingsRow);
            // Load category visibility section async (needs API calls)
            this._loadUserCatVisPanel(id, username);
        }
        // Clear the old external form area
        const area = document.getElementById('user-mgmt-form-area');
        if (area) area.innerHTML = '';
    },

    cancelEditUser(id) {
        document.getElementById(`child-settings-row-${id}`)?.remove();
        this.navigate('settings');
    },

    _onNewRoleChange() {
        const role = document.getElementById('new-role')?.value;
        const area = document.getElementById('new-child-settings-area');
        if (!area) return;
        const gd = { showMusic:true, showPictures:true, showMovies:true, showTvShows:true, showMusicVideos:true,
            showRadio:true, showInternetTV:true, showEBooks:true, showAudioBooks:true, showPodcasts:true, showAnime:true };
        if (role === 'child') {
            this._currentEditChildSettings = { ...gd, allowPG13:false, allowTV14:false };
            area.innerHTML = this._renderChildSettingsHtml('new', null);
        } else if (role === 'guest') {
            this._currentEditChildSettings = { ...gd };
            area.innerHTML = this._renderGuestSettingsHtml('new', null);
        } else {
            this._currentEditChildSettings = null;
            area.innerHTML = '';
        }
    },

    _onEditRoleChange(id) {
        const role = document.getElementById(`edit-role-${id}`)?.value;
        const row = document.getElementById(`user-row-${id}`);
        if (!row) return;
        // Remove existing child settings row
        document.getElementById(`child-settings-row-${id}`)?.remove();
        const gd = { showMusic:true, showPictures:true, showMovies:true, showTvShows:true, showMusicVideos:true,
            showRadio:true, showInternetTV:true, showEBooks:true, showAudioBooks:true, showPodcasts:true, showAnime:true };
        if (role === 'child' || role === 'guest') {
            // Restore saved settings if the user originally had this same role
            const userEntry = (this._adminUsers || []).find(u => u.id === id);
            let savedCs = null;
            if (userEntry && userEntry.role === role) {
                const rawCs = userEntry.childSettings;
                savedCs = rawCs ? (() => { try { return JSON.parse(rawCs); } catch(e) { return null; } })() : null;
            }
            const defaults = role === 'child' ? { ...gd, allowPG13:false, allowTV14:false } : { ...gd };
            this._currentEditChildSettings = Object.assign({}, defaults, savedCs || {});
            let panelHtml = '';
            if (role === 'child') panelHtml = this._renderChildSettingsHtml(id, savedCs || this._currentEditChildSettings);
            else panelHtml = this._renderGuestSettingsHtml(id, savedCs || this._currentEditChildSettings);
            const settingsRow = document.createElement('tr');
            settingsRow.id = `child-settings-row-${id}`;
            settingsRow.innerHTML = `<td colspan="5" class="child-settings-inline-cell">${panelHtml}</td>`;
            row.insertAdjacentElement('afterend', settingsRow);
            const userEntry2 = (this._adminUsers || []).find(u => u.id === id);
            if (userEntry2) this._loadUserCatVisPanel(id, userEntry2.username);
        } else {
            this._currentEditChildSettings = null;
        }
    },

    _onChildSettingChange(key, value) {
        if (!this._currentEditChildSettings) this._currentEditChildSettings = {};
        this._currentEditChildSettings[key] = value;
    },

    _toggleViewingHoursInputs(id, enabled) {
        const el = document.getElementById(`viewing-hours-inputs-${id}`);
        if (el) el.style.display = enabled ? '' : 'none';
    },

    async _loadUserCatVisPanel(id, username) {
        const placeholder = document.getElementById(`cat-vis-panel-${id}`);
        if (!placeholder) return;
        const [musicCats, videoCats, userSettings, cgMusic, cgMovie, cgTv, cgAnime] = await Promise.all([
            this.api('tracks/custom-categories'),
            this.api('videos/custom-categories'),
            this.userRole === 'admin' ? this.api(`category-settings/user/${username}`) : null,
            this.api('custom-genres?domain=music'),
            this.api('custom-genres?domain=movie'),
            this.api('custom-genres?domain=tv'),
            this.api('custom-genres?domain=anime')
        ]);
        const allCustomGenres = [...(cgMusic||[]), ...(cgMovie||[]), ...(cgTv||[]), ...(cgAnime||[])];
        const hiddenMusic = new Set((userSettings?.music?.hidden || []).map(s => s.toLowerCase()));
        const hiddenVideo = new Set((userSettings?.video?.hidden || []).map(s => s.toLowerCase()));
        const hiddenCgIds = new Set((userSettings?.hiddenCustomGenres || []).map(s => s.toLowerCase()));

        let html = '';
        if ((musicCats && musicCats.length > 0) || (videoCats && videoCats.length > 0)) {
            html += `<h4 style="margin-top:16px">${this.t('catSettings.hiddenCats')}</h4>
                <p class="child-settings-hint">${this.t('catSettings.hiddenCatsHint')}</p>`;

            if (musicCats && musicCats.length > 0) {
                html += `<p style="font-size:12px;font-weight:600;color:var(--text-secondary);margin:10px 0 6px">${this.t('nav.music')}</p>
                    <div class="child-settings-grid">`;
                musicCats.forEach(c => {
                    const isHidden = hiddenMusic.has(c.name.toLowerCase());
                    html += `<label class="child-setting-toggle">
                        <input type="checkbox" class="cat-vis-music-${id}" data-cat="${this.esc(c.name)}" ${isHidden ? 'checked' : ''}>
                        <span>${this.esc(c.name)}</span>
                    </label>`;
                });
                html += `</div>`;
            }

            if (videoCats && videoCats.length > 0) {
                html += `<p style="font-size:12px;font-weight:600;color:var(--text-secondary);margin:10px 0 6px">${this.t('page.movies')} / ${this.t('page.tvShows')}</p>
                    <div class="child-settings-grid">`;
                videoCats.forEach(c => {
                    const isHidden = hiddenVideo.has(c.name.toLowerCase());
                    html += `<label class="child-setting-toggle">
                        <input type="checkbox" class="cat-vis-video-${id}" data-cat="${this.esc(c.name)}" ${isHidden ? 'checked' : ''}>
                        <span>${this.esc(c.name)}</span>
                    </label>`;
                });
                html += `</div>`;
            }
        }

        if (allCustomGenres.length > 0) {
            html += `<h4 style="margin-top:16px">${this.t('customGenres.hiddenTitle')}</h4>
                <p class="child-settings-hint">${this.t('customGenres.hiddenHint')}</p>
                <div class="child-settings-grid">`;
            allCustomGenres.forEach(cg => {
                const isHidden = hiddenCgIds.has(cg.id.toLowerCase());
                const domainLabel = cg.domain === 'music' ? this.t('nav.music')
                    : cg.domain === 'movie' ? this.t('page.movies')
                    : cg.domain === 'tv' ? this.t('page.tvShows')
                    : this.t('nav.anime');
                html += `<label class="child-setting-toggle">
                    <input type="checkbox" class="cg-vis-${id}" data-cgid="${this.esc(cg.id)}" ${isHidden ? 'checked' : ''}>
                    <span>${this.esc(cg.name)} <small style="color:var(--text-secondary)">(${domainLabel})</small></span>
                </label>`;
            });
            html += `</div>`;
        }

        placeholder.innerHTML = html || '';
    },

    _renderChildSettingsHtml(id, saved) {
        const sections = [
            ['showMusic',       this.t('nav.music')],
            ['showPictures',    this.t('nav.pictures')],
            ['showMovies',      this.t('nav.movies')],
            ['showTvShows',     this.t('nav.tvShows')],
            ['showMusicVideos', this.t('settings.musicVideos')],
            ['showRadio',       this.t('settings.radio')],
            ['showInternetTV',  this.t('settings.internetTv')],
            ['showEBooks',      this.t('settings.ebooks')],
            ['showAudioBooks',  this.t('settings.audioBooks')],
            ['showPodcasts',    this.t('settings.podcasts')],
            ['showAnime',       this.t('nav.anime')],
        ];
        const checks = sections.map(([key, label]) => {
            const checked = saved ? (saved[key] ?? true) : true;
            return `<label class="child-setting-toggle">
                <input type="checkbox" id="cs-${id}-${key}" ${checked ? 'checked' : ''} onchange="App._onChildSettingChange('${key}', this.checked)">
                <span>${this.esc(label)}</span>
            </label>`;
        }).join('');
        const ratingChecks = [
            ['allowPG13', this.t('settings.allowPG13')],
            ['allowTV14', this.t('settings.allowTV14')],
        ].map(([key, label]) => {
            const checked = saved ? (saved[key] ?? false) : false;
            return `<label class="child-setting-toggle">
                <input type="checkbox" id="cs-${id}-${key}" ${checked ? 'checked' : ''} onchange="App._onChildSettingChange('${key}', this.checked)">
                <span>${this.esc(label)}</span>
            </label>`;
        }).join('');

        const vhEnabled = saved ? (saved.viewingHoursEnabled ?? false) : false;
        const vhStart   = saved ? (saved.viewingHoursStart   ?? '08:00') : '08:00';
        const vhEnd     = saved ? (saved.viewingHoursEnd     ?? '22:00') : '22:00';
        const viewingHoursHtml = `
            <h4 style="margin-top:16px">${this.t('settings.viewingHours')}</h4>
            <p class="child-settings-hint">${this.t('settings.viewingHoursHint')}</p>
            <div class="child-settings-grid">
                <label class="child-setting-toggle">
                    <input type="checkbox" id="cs-${id}-viewingHoursEnabled" ${vhEnabled ? 'checked' : ''}
                           onchange="App._toggleViewingHoursInputs('${id}', this.checked)">
                    <span>${this.t('settings.viewingHoursEnabled')}</span>
                </label>
            </div>
            <div id="viewing-hours-inputs-${id}" class="viewing-hours-row" style="${vhEnabled ? '' : 'display:none'}">
                <label>${this.t('settings.viewingHoursFrom')}
                    <input type="time" id="cs-${id}-viewingHoursStart" value="${vhStart}" class="user-mgmt-inline-input">
                </label>
                <label>${this.t('settings.viewingHoursTo')}
                    <input type="time" id="cs-${id}-viewingHoursEnd" value="${vhEnd}" class="user-mgmt-inline-input">
                </label>
            </div>`;

        return `<div class="child-settings-panel">
            <h4>${this.t('settings.childSectionAccess')}</h4>
            <p class="child-settings-hint">${this.t('settings.childSectionAccessHint')}</p>
            <div class="child-settings-grid">${checks}</div>
            <h4 style="margin-top:16px">${this.t('settings.ageRatings')}</h4>
            <p class="child-settings-hint">${this.t('settings.ageRatingsHint')}</p>
            <div class="child-settings-grid">${ratingChecks}</div>
            ${viewingHoursHtml}
            <div id="cat-vis-panel-${id}"><span style="font-size:12px;color:var(--text-muted)">Loading folder categories…</span></div>
        </div>`;
    },

    _renderGuestSettingsHtml(id, saved) {
        const sections = [
            ['showMusic',       this.t('nav.music')],
            ['showPictures',    this.t('nav.pictures')],
            ['showMovies',      this.t('nav.movies')],
            ['showTvShows',     this.t('nav.tvShows')],
            ['showMusicVideos', this.t('settings.musicVideos')],
            ['showRadio',       this.t('settings.radio')],
            ['showInternetTV',  this.t('settings.internetTv')],
            ['showEBooks',      this.t('settings.ebooks')],
            ['showAudioBooks',  this.t('settings.audioBooks')],
            ['showPodcasts',    this.t('settings.podcasts')],
            ['showAnime',       this.t('nav.anime')],
        ];
        const checks = sections.map(([key, label]) => {
            const checked = saved ? (saved[key] ?? true) : true;
            return `<label class="child-setting-toggle">
                <input type="checkbox" id="cs-${id}-${key}" ${checked ? 'checked' : ''} onchange="App._onChildSettingChange('${key}', this.checked)">
                <span>${this.esc(label)}</span>
            </label>`;
        }).join('');
        return `<div class="child-settings-panel">
            <h4>${this.t('settings.guestSectionAccess')}</h4>
            <p class="child-settings-hint">${this.t('settings.guestSectionAccessHint')}</p>
            <div class="child-settings-grid">${checks}</div>
            <div id="cat-vis-panel-${id}"><span style="font-size:12px;color:var(--text-muted)">Loading folder categories…</span></div>
        </div>`;
    },

    async submitEditUser(id) {
        const displayName = document.getElementById(`edit-displayname-${id}`)?.value?.trim();
        const userType = document.getElementById(`edit-role-${id}`)?.value;
        const pin = document.getElementById(`edit-pin-${id}`)?.value;

        const body = { displayName, userType };
        if (pin && pin.length > 0) {
            if (!/^\d{6}$/.test(pin)) { alert('PIN must be exactly 6 digits'); return; }
            body.pin = pin;
        }

        if (userType === 'child' || userType === 'guest') {
            const sectionKeys = ['showMusic','showPictures','showMovies','showTvShows','showMusicVideos',
                                 'showRadio','showInternetTV','showEBooks','showAudioBooks','showPodcasts','showAnime'];
            const cs = {};
            sectionKeys.forEach(key => {
                const el = document.getElementById(`cs-${id}-${key}`);
                cs[key] = el ? el.checked : true;
            });
            if (userType === 'child') {
                ['allowPG13','allowTV14'].forEach(key => {
                    const el = document.getElementById(`cs-${id}-${key}`);
                    cs[key] = el ? el.checked : false;
                });
                cs.viewingHoursEnabled = document.getElementById(`cs-${id}-viewingHoursEnabled`)?.checked ?? false;
                cs.viewingHoursStart   = document.getElementById(`cs-${id}-viewingHoursStart`)?.value   ?? '08:00';
                cs.viewingHoursEnd     = document.getElementById(`cs-${id}-viewingHoursEnd`)?.value     ?? '22:00';
            }
            body.childSettings = JSON.stringify(cs);
        } else {
            body.childSettings = '';   // clear if role changed to admin
        }

        // Save per-user category visibility settings if panel was loaded
        const userEntry = (this._adminUsers || []).find(u => u.id === id);
        if (userEntry) {
            const hiddenMusic = [];
            document.querySelectorAll(`.cat-vis-music-${id}`).forEach(el => {
                if (el.checked) hiddenMusic.push(el.dataset.cat);
            });
            const hiddenVideo = [];
            document.querySelectorAll(`.cat-vis-video-${id}`).forEach(el => {
                if (el.checked) hiddenVideo.push(el.dataset.cat);
            });
            const hiddenCg = [];
            document.querySelectorAll(`.cg-vis-${id}`).forEach(el => {
                if (el.checked) hiddenCg.push(el.dataset.cgid);
            });
            // Only save if the panel was populated (at least one checkbox found)
            const hasMusicPanel = document.querySelector(`.cat-vis-music-${id}`) !== null;
            const hasVideoPanel = document.querySelector(`.cat-vis-video-${id}`) !== null;
            const hasCgPanel = document.querySelector(`.cg-vis-${id}`) !== null;
            if (hasMusicPanel || hasVideoPanel || hasCgPanel) {
                // Preserve excludedFromLibrary from user's own settings
                const currentCats = await this.api(`category-settings/user/${userEntry.username}`);
                const catBody = {
                    music: { excludedFromLibrary: currentCats?.music?.excludedFromLibrary || [], hidden: hiddenMusic },
                    video: { hidden: hiddenVideo },
                    hiddenCustomGenres: hiddenCg
                };
                await this.apiPost(`category-settings/user/${userEntry.username}`, catBody);
            }
        }

        const result = await this.apiPut(`auth/users/${id}`, body);
        if (result && result.message) {
            this.navigate('settings');
        } else if (result && result.error) {
            alert(result.error);
        }
    },

    async deleteUser(id, username) {
        if (!confirm(`Delete user "${username}"? This will also remove their user database.`)) return;
        const result = await this.apiDelete(`auth/users/${id}`);
        if (result && result.message) {
            this.navigate('settings');
        } else if (result && result.error) {
            alert(result.error);
        }
    },

    async logout() {
        await this.apiPost('auth/logout');
        window.location.href = '/login.html';
    },

    // ─── Library Scan ────────────────────────────────────────
    async startScan() {
        const result = await this.apiPost('scan');
        if (result) this.pollScanStatus();
    },

    async pollScanStatus() {
        const poll = async () => {
            const s = await this.api('scan/status');
            const statusEl = document.getElementById('scan-status');
            if (!s) return;
            if (statusEl) {
                if (s.isScanning) {
                    statusEl.innerHTML = `<div class="scan-bar">
                        <div class="scan-text">${this.t('status.scanning')} ${s.processedFiles}/${s.totalFiles} files (${s.percentComplete}%)</div>
                        <div class="scan-progress"><div class="scan-progress-fill" style="width:${s.percentComplete}%"></div></div>
                    </div>`;
                } else {
                    statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${s.message || 'Scan complete'}</div></div>`;
                }
            }
            if (s.isScanning) setTimeout(poll, 1000);
        };
        poll();
    },

    // ─── Pictures Scan ─────────────────────────────────────
    async startPictureScan() {
        const result = await this.apiPost('scan/pictures');
        if (result) this.pollPictureScanStatus();
    },

    async pollPictureScanStatus() {
        const poll = async () => {
            const s = await this.api('scan/pictures/status');
            const statusEl = document.getElementById('pic-scan-status');
            if (!s) return;
            if (statusEl) {
                if (s.isScanning) {
                    statusEl.innerHTML = `<div class="scan-bar">
                        <div class="scan-text">${this.t('status.scanning')} ${s.processedFiles}/${s.totalFiles} files (${s.percentComplete}%)</div>
                        <div class="scan-progress"><div class="scan-progress-fill" style="width:${s.percentComplete}%"></div></div>
                    </div>`;
                } else {
                    statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${s.message || 'Scan complete'}</div></div>`;
                    this.loadBadgeCounts();
                }
            }
            if (s.isScanning) setTimeout(poll, 1000);
        };
        poll();
    },

    // ─── eBooks Scan ──────────────────────────────────
    async startEBookScan() {
        const result = await this.apiPost('scan/ebooks');
        if (result) this.pollEBookScanStatus();
    },

    async pollEBookScanStatus() {
        const poll = async () => {
            const s = await this.api('scan/ebooks/status');
            const statusEl = document.getElementById('ebook-scan-status');
            if (!s) return;
            if (statusEl) {
                if (s.isScanning) {
                    statusEl.innerHTML = `<div class="scan-bar">
                        <div class="scan-text">${this.t('status.scanning')} ${s.processedFiles}/${s.totalFiles} files (${s.percentComplete}%)</div>
                        <div class="scan-progress"><div class="scan-progress-fill" style="width:${s.percentComplete}%"></div></div>
                    </div>`;
                } else {
                    statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${s.message || 'Scan complete'}</div></div>`;
                    this.loadBadgeCounts();
                }
            }
            if (s.isScanning) setTimeout(poll, 1000);
        };
        poll();
    },

    // ─── Audio Books Scan ──────────────────────────
    async startAudioBookScan() {
        const result = await this.apiPost('scan/audiobooks');
        if (result) this.pollAudioBookScanStatus();
    },

    async pollAudioBookScanStatus() {
        const poll = async () => {
            const s = await this.api('scan/audiobooks/status');
            const statusEl = document.getElementById('audiobook-scan-status');
            if (!s) return;
            if (statusEl) {
                if (s.isScanning) {
                    statusEl.innerHTML = `<div class="scan-bar">
                        <div class="scan-text">${this.t('status.scanning')} ${s.processedFiles}/${s.totalFiles} files (${s.percentComplete}%)</div>
                        <div class="scan-progress"><div class="scan-progress-fill" style="width:${s.percentComplete}%"></div></div>
                    </div>`;
                } else {
                    statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${s.message || 'Scan complete'}</div></div>`;
                    this.loadBadgeCounts();
                }
            }
            if (s.isScanning) setTimeout(poll, 1000);
        };
        poll();
    },

    // ─── Music Videos Scan ──────────────────────────
    async startMvScan() {
        const result = await this.apiPost('scan/musicvideos');
        if (result) this.pollMvScanStatus();
    },

    async pollMvScanStatus() {
        const poll = async () => {
            const s = await this.api('scan/musicvideos/status');
            const statusEl = document.getElementById('mv-scan-status');
            if (!s) return;
            if (statusEl) {
                if (s.isScanning) {
                    statusEl.innerHTML = `<div class="scan-bar">
                        <div class="scan-text">${this.t('status.scanning')} ${s.processedFiles}/${s.totalFiles} files (${s.percentComplete}%)</div>
                        <div class="scan-progress"><div class="scan-progress-fill" style="width:${s.percentComplete}%"></div></div>
                    </div>`;
                } else {
                    statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${s.message || 'Scan complete'}</div></div>`;
                    this.loadBadgeCounts();
                }
            }
            if (s.isScanning) setTimeout(poll, 1000);
        };
        poll();
    },

    async generateMvThumbnails() {
        const result = await this.apiPost('musicvideo/generate-thumbnails');
        const statusEl = document.getElementById('mv-scan-status');
        if (statusEl && result) statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${result.message || 'Generating thumbnails...'}</div></div>`;
    },

    // ─── Videos (Movies/TV) Scan ────────────────────────
    async startVideoScan(scope) {
        const url = scope ? `scan/videos?scope=${scope}` : 'scan/videos';
        const result = await this.apiPost(url);
        if (result) this.pollVideoScanStatus();
    },

    async pollVideoScanStatus() {
        const poll = async () => {
            const s = await this.api('scan/videos/status');
            const statusEl = document.getElementById('video-scan-status');
            if (!s) return;
            if (statusEl) {
                if (s.isScanning) {
                    statusEl.innerHTML = `<div class="scan-bar">
                        <div class="scan-text">${this.t('status.scanning')} ${s.processedFiles}/${s.totalFiles} files (${s.percentComplete}%)</div>
                        <div class="scan-progress"><div class="scan-progress-fill" style="width:${s.percentComplete}%"></div></div>
                    </div>`;
                } else {
                    statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${s.message || 'Scan complete'}</div></div>`;
                    this.loadBadgeCounts();
                }
            }
            if (s.isScanning) setTimeout(poll, 1000);
        };
        poll();
    },

    async generateVideoThumbnails() {
        const result = await this.apiPost('video/generate-thumbnails');
        const statusEl = document.getElementById('video-scan-status');
        if (statusEl && result) statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${result.message || 'Generating thumbnails...'}</div></div>`;
    },

    async refreshHdrMetadata() {
        const statusEl = document.getElementById('video-scan-status');
        if (statusEl) statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">Starting HDR metadata refresh...</div></div>`;
        const result = await this.apiPost('videos/refresh-hdr');
        if (result) {
            if (statusEl) statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${result.message}</div></div>`;
        } else {
            if (statusEl) statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text" style="color:var(--error)">Failed to start HDR refresh. Check that ffprobe is available.</div></div>`;
        }
    },

    // ─── Global Scan Dialog & Progress ────────────────
    showScanDialog() {
        // Remove any existing dialog
        document.querySelectorAll('.scan-dialog-overlay').forEach(el => el.remove());

        const overlay = document.createElement('div');
        overlay.className = 'scan-dialog-overlay';
        overlay.innerHTML = `
            <div class="scan-dialog">
                <div class="scan-dialog-title">Rescan Library</div>
                <p class="scan-dialog-desc">Select which libraries to rescan. This will look for new, modified, or removed files in your configured folders.</p>
                <div class="scan-dialog-options">
                    <label class="scan-dialog-option"><input type="checkbox" value="music" checked> <svg class="scan-dialog-icon"><use href="#icon-music"/></svg> <span>Music</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="movies" checked> <svg class="scan-dialog-icon"><use href="#icon-film"/></svg> <span>${this.t('nav.movies')}</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="tvshows" checked> <svg class="scan-dialog-icon"><use href="#icon-tv"/></svg> <span>${this.t('nav.tvShows')}</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="musicvideos" checked> <svg class="scan-dialog-icon"><use href="#icon-video"/></svg> <span>Music Videos</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="pictures" checked> <svg class="scan-dialog-icon"><use href="#icon-image"/></svg> <span>Pictures</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="ebooks" checked> <svg class="scan-dialog-icon"><use href="#icon-book"/></svg> <span>eBooks</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="audiobooks" checked> <svg class="scan-dialog-icon"><use href="#icon-headphones"/></svg> <span>${this.t('nav.audioBooks')}</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="anime" checked> <svg class="scan-dialog-icon"><use href="#icon-film"/></svg> <span>${this.t('nav.anime')}</span></label>
                </div>
                <div class="scan-dialog-buttons">
                    <button class="scan-dialog-cancel" onclick="this.closest('.scan-dialog-overlay').remove()">Cancel</button>
                    <button class="scan-dialog-start" onclick="App.startGlobalScan()">Start Scan</button>
                </div>
            </div>`;
        overlay.addEventListener('click', e => { if (e.target === overlay) overlay.remove(); });
        document.body.appendChild(overlay);
    },

    async startGlobalScan() {
        const overlay = document.querySelector('.scan-dialog-overlay');
        let checked = [...overlay.querySelectorAll('input[type=checkbox]:checked')].map(cb => cb.value);
        overlay.remove();

        if (checked.length === 0) return;

        // Merge movies+tvshows into a single combined scan (they share one scanner)
        const hasMovies  = checked.includes('movies');
        const hasTvShows = checked.includes('tvshows');
        if (hasMovies && hasTvShows) {
            checked = checked.filter(t => t !== 'movies' && t !== 'tvshows');
            checked.push('videos'); // combined — no scope restriction
        } else if (hasMovies) {
            checked = checked.map(t => t === 'movies' ? 'movies' : t);
        } else if (hasTvShows) {
            checked = checked.map(t => t === 'tvshows' ? 'tvshows' : t);
        }

        // Create global progress banner
        this._globalScanTypes = checked;
        this._globalScanDone = new Set();
        this._globalScanStatus = {};
        this._showGlobalScanBanner();

        // Start all selected scans
        const scanEndpoints = {
            music: 'scan',
            videos: 'scan/videos',
            movies: 'scan/videos?scope=movies',
            tvshows: 'scan/videos?scope=tvshows',
            musicvideos: 'scan/musicvideos',
            pictures: 'scan/pictures',
            ebooks: 'scan/ebooks',
            audiobooks: 'scan/audiobooks',
            anime: 'scan/anime'
        };
        const statusEndpoints = {
            music: 'scan/status',
            videos: 'scan/videos/status',
            movies: 'scan/videos/status',
            tvshows: 'scan/videos/status',
            musicvideos: 'scan/musicvideos/status',
            pictures: 'scan/pictures/status',
            ebooks: 'scan/ebooks/status',
            audiobooks: 'scan/audiobooks/status',
            anime: 'scan/anime/status'
        };
        const labels = {
            music: 'Music',
            videos: this.t('nav.movies') + ' & ' + this.t('nav.tvShows'),
            movies: this.t('nav.movies'),
            tvshows: this.t('nav.tvShows'),
            musicvideos: 'Music Videos',
            pictures: 'Pictures',
            ebooks: 'eBooks',
            audiobooks: this.t('nav.audioBooks'),
            anime: this.t('nav.anime')
        };

        for (const type of checked) {
            await this.apiPost(scanEndpoints[type]);
        }

        // Poll all scans
        const pollAll = async () => {
            let anyActive = false;
            for (const type of checked) {
                if (this._globalScanDone.has(type)) continue;
                const s = await this.api(statusEndpoints[type]);
                if (!s) continue;
                if (s.isScanning) {
                    anyActive = true;
                    this._globalScanStatus[type] = { label: labels[type], pct: s.percentComplete, processed: s.processedFiles, total: s.totalFiles };
                } else {
                    this._globalScanDone.add(type);
                    this._globalScanStatus[type] = { label: labels[type], pct: 100, done: true, message: s.message };
                }
            }
            this._updateGlobalScanBanner();
            if (anyActive) {
                setTimeout(pollAll, 1000);
            } else {
                this._finishGlobalScanBanner();
                this.loadBadgeCounts();
            }
        };
        setTimeout(pollAll, 500);
    },

    _showGlobalScanBanner() {
        document.querySelectorAll('.global-scan-banner').forEach(el => el.remove());
        const banner = document.createElement('div');
        banner.className = 'global-scan-banner';
        banner.innerHTML = `<div class="global-scan-inner">
            <span class="global-scan-spinner"></span>
            <span class="global-scan-text">Starting library scan...</span>
            <button class="global-scan-close" onclick="this.closest('.global-scan-banner').remove()" title="Dismiss">&times;</button>
        </div>
        <div class="global-scan-details"></div>`;
        document.body.appendChild(banner);
    },

    _updateGlobalScanBanner() {
        const banner = document.querySelector('.global-scan-banner');
        if (!banner) return;
        const types = this._globalScanTypes || [];
        const doneCount = this._globalScanDone.size;
        const totalCount = types.length;

        let detailsHtml = '';
        for (const type of types) {
            const s = this._globalScanStatus[type];
            if (!s) continue;
            const icon = s.done ? '&#10003;' : '&#9679;';
            const iconClass = s.done ? 'scan-item-done' : 'scan-item-active';
            const info = s.done ? 'Complete' : `${s.processed}/${s.total} (${s.pct}%)`;
            detailsHtml += `<div class="global-scan-item ${iconClass}"><span>${icon} ${s.label}</span><span>${info}</span></div>`;
        }
        banner.querySelector('.global-scan-details').innerHTML = detailsHtml;

        const activeTypes = types.filter(t => !this._globalScanDone.has(t));
        const text = activeTypes.length > 0
            ? `Scanning ${doneCount}/${totalCount} complete...`
            : 'Scan complete!';
        banner.querySelector('.global-scan-text').textContent = text;
    },

    _finishGlobalScanBanner() {
        const banner = document.querySelector('.global-scan-banner');
        if (!banner) return;
        banner.querySelector('.global-scan-text').textContent = 'All scans complete!';
        const spinner = banner.querySelector('.global-scan-spinner');
        if (spinner) spinner.className = 'global-scan-check';
        // Auto-dismiss after 5 seconds
        setTimeout(() => { if (banner.parentNode) banner.remove(); }, 5000);
    },

    // ─── Music Videos Library ─────────────────────────
    async renderMusicVideos(el) {
        this.mvPage = 1;
        this.mvSort = 'recent';
        this.mvArtist = null;
        this._mvArtistPage = 0;
        await this.loadMvPage(el);
    },

    async loadMvPage(el) {
        const target = el || document.getElementById('main-content');
        let url = `musicvideos?limit=${this.mvPerPage}&page=${this.mvPage}&sort=${this.mvSort}`;
        if (this.mvArtist) url += `&artist=${encodeURIComponent(this.mvArtist)}`;

        const [data, artists, stats] = await Promise.all([
            this.api(url),
            this.api('musicvideos/artists'),
            this.api('musicvideos/stats')
        ]);

        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load music videos.'); return; }
        this.mvTotal = data.total;
        const totalPages = Math.ceil(data.total / this.mvPerPage);

        let html = `<div class="page-header"><h1>${this.t('page.musicVideos')}</h1>
            <div class="filter-bar">
            <button class="mv-shuffle-btn" onclick="App.shuffleMusicVideo()" title="Play a random music video">
                <svg><use href="#icon-shuffle"/></svg> ${this.t('btn.shufflePlay')}
            </button>`;
        const sortLabels = { recent: this.t('sort.recent'), title: this.t('sort.title'), artist: this.t('sort.artist'), year: this.t('sort.year'), size: this.t('sort.size'), duration: this.t('sort.duration') };
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.mvSort === key ? ' active' : ''}" onclick="App.changeMvSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

        // Stats bar
        if (stats && stats.totalVideos > 0) {
            html += `<div class="mv-stats-bar">
                <span>${stats.totalVideos} videos</span>
                <span>${this.formatSize(stats.totalSize)}</span>
                <span>${this.formatDuration(stats.totalDuration)}</span>
                <span>${stats.totalArtists} artists</span>
                ${stats.needsOptimization > 0 ? `<span class="mv-stat-warn">${stats.needsOptimization} need optimization</span>` : ''}
                ${!stats.ffmpegAvailable ? '<span class="mv-stat-warn">FFmpeg not found</span>' : ''}
            </div>`;
        }

        // Artist filter chips
        this._mvArtists = artists || [];
        html += this._buildMvArtistChips();

        if (data.videos && data.videos.length > 0) {
            if (totalPages > 1) {
                html += `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;color:var(--text-secondary);font-size:13px">
                    <span>${data.total} videos${this.mvArtist ? ' by ' + this.esc(this.mvArtist) : ''}</span>
                    <span>Page ${this.mvPage} of ${totalPages}</span>
                </div>`;
            }

            html += '<div class="mv-grid">';
            data.videos.forEach(v => {
                const thumbSrc = v.thumbnailPath ? `/mvthumb/${v.thumbnailPath}` : '';
                const dur = this.formatDuration(v.duration);
                const formatClass = v.mp4Compliant ? 'mv-format-ok' : (v.needsOptimization ? 'mv-format-warn' : 'mv-format-badge');
                const mvFavClass = v.isFavourite ? 'active' : '';
                html += `<div class="mv-card" onclick="App.openMvDetail(${v.id})" data-mv-id="${v.id}">
                    <div class="mv-card-thumb">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none">&#127909;</span>`
                            : `<span class="mv-card-placeholder">&#127909;</span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        <span class="mv-format-badge ${formatClass}">${this.esc(v.format)}</span>
                        <button class="mv-card-play" onclick="event.stopPropagation(); App.playMusicVideo(${v.id})">&#9654;</button>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${this.esc(v.artist)}</div>
                        <div class="mv-card-meta">
                            <span>${v.resolution || ''}</span>
                            <span>${this.formatSize(v.sizeBytes)}</span>
                        </div>
                    </div>
                    <button class="song-card-fav ${mvFavClass}" onclick="event.stopPropagation(); App.toggleMvFav(${v.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
            html += this.renderMvPagination(this.mvPage, totalPages);
        } else {
            html += this.emptyState(this.t('empty.noMusicVideos.title'), this.t('empty.noMusicVideos.desc'));
        }

        target.innerHTML = html;
    },

    changeMvSort(sort) {
        this.mvSort = sort;
        this.mvPage = 1;
        this.loadMvPage();
    },

    filterMvArtist(artist) {
        this.mvArtist = artist;
        this.mvPage = 1;
        this.loadMvPage();
    },

    _buildMvArtistChips() {
        const artists = this._mvArtists || [];
        if (artists.length === 0) return '';
        const perPage = 10;
        const totalPages = Math.ceil(artists.length / perPage);
        const page = this._mvArtistPage;
        const slice = artists.slice(page * perPage, (page + 1) * perPage);
        let html = '<div class="mv-artist-chips" id="mv-artist-chips">';
        html += `<button class="filter-chip${!this.mvArtist ? ' active' : ''}" onclick="App.filterMvArtist(null)">${this.t('filter.allArtists')}</button>`;
        if (totalPages > 1) {
            html += `<button class="mv-artist-nav${page <= 0 ? ' disabled' : ''}" onclick="App.navMvArtistPage(-1)" title="Previous artists" ${page <= 0 ? 'disabled' : ''}>&#8249;</button>`;
        }
        slice.forEach(a => {
            const isActive = this.mvArtist === a.name;
            html += `<button class="filter-chip${isActive ? ' active' : ''}" onclick="App.filterMvArtist('${this.esc(a.name)}')">${this.esc(a.name)} <span style="opacity:.6">${a.count}</span></button>`;
        });
        if (totalPages > 1) {
            html += `<button class="mv-artist-nav${page >= totalPages - 1 ? ' disabled' : ''}" onclick="App.navMvArtistPage(1)" title="Next artists" ${page >= totalPages - 1 ? 'disabled' : ''}>&#8250;</button>`;
            html += `<span class="mv-artist-nav-label">${page + 1} / ${totalPages}</span>`;
        }
        html += '</div>';
        return html;
    },

    navMvArtistPage(dir) {
        const totalPages = Math.ceil((this._mvArtists || []).length / 10);
        this._mvArtistPage = Math.max(0, Math.min(totalPages - 1, this._mvArtistPage + dir));
        const container = document.getElementById('mv-artist-chips');
        if (container) container.outerHTML = this._buildMvArtistChips();
    },

    goMvPage(page) {
        const totalPages = Math.ceil(this.mvTotal / this.mvPerPage);
        if (page < 1 || page > totalPages) return;
        this.mvPage = page;
        this.loadMvPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    renderMvPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goMvPage(${currentPage - 1})">&laquo; ${this.t('pagination.prev')}</button>`;
        const pages = [];
        pages.push(1);
        let rangeStart = Math.max(2, currentPage - 2);
        let rangeEnd = Math.min(totalPages - 1, currentPage + 2);
        if (rangeStart > 2) pages.push('...');
        for (let p = rangeStart; p <= rangeEnd; p++) pages.push(p);
        if (rangeEnd < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goMvPage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goMvPage(${currentPage + 1})">${this.t('pagination.next')} &raquo;</button>`;
        html += '</div>';
        return html;
    },

    _mvShuffleMode: false,

    async openMvDetail(id) {
        this.stopAllMedia();
        const [video, mvRatingSum] = await Promise.all([
            this.api(`musicvideos/${id}`),
            this.api(`ratings/summary/musicvideo/${id}`)
        ]);
        if (!video) return;

        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.t('page.musicVideos')}</span>`;

        const mvActionsAndPopup = `<div style="flex-shrink:0;margin-top:28px;position:relative">
            <div class="vp-actions-bar" style="justify-content:flex-end">
                <button id="btn-video-details" class="mv-stats-btn" onclick="App.toggleVideoDetailsPopup()"><svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-list"/></svg> Video Details</button>
                <button class="vp-fav-btn${video.isFavourite ? ' active' : ''}" id="vp-fav-btn" onclick="App.toggleMvFav(${id}, this)" title="Add to Favourites">&#10084;</button>
                <div class="rating-widget vp-inline-rating" data-mt="musicvideo" data-mid="${id}">${this._buildRatingWidgetInner(mvRatingSum || {}, 'musicvideo', id)}</div>
                <button onclick="App.toggleEQPanel()" id="btn-eq-page" class="mv-stats-btn"><svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-equalizer"/></svg> ${this.t('player.equalizer', 'Equalizer')}</button>
            </div>
            <div id="video-details-popup" class="vp-details-popup" style="display:none">
                <div class="setting-row setting-row-col"><span class="setting-label">Filename</span><span class="setting-value" style="word-break:break-all;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;line-height:1.4">${this.esc(video.fileName)}</span></div>
                <div class="setting-row"><span class="setting-label">Format</span><span class="setting-value">${this.esc(video.format)}</span></div>
                <div class="setting-row"><span class="setting-label">Resolution</span><span class="setting-value">${video.resolution || 'Unknown'} (${video.width}x${video.height})</span></div>
                <div class="setting-row"><span class="setting-label">Codec</span><span class="setting-value">${this.esc(video.codec || 'Unknown')}</span></div>
                ${video.bitrate > 0 ? `<div class="setting-row"><span class="setting-label">Bitrate</span><span class="setting-value">${Math.round(video.bitrate / 1000)} kbps</span></div>` : ''}
                ${video.audioChannels > 0 ? `<div class="setting-row"><span class="setting-label">Audio</span><span class="setting-value">${video.audioChannels} channel${video.audioChannels > 1 ? 's' : ''}${video.audioChannels > 2 ? ' (surround)' : ''}</span></div>` : ''}
                ${video.genre ? `<div class="setting-row"><span class="setting-label">Genre</span><span class="setting-value">${this.esc(video.genre)}</span></div>` : ''}
                ${video.album ? `<div class="setting-row"><span class="setting-label">Album</span><span class="setting-value">${this.esc(video.album)}</span></div>` : ''}
                <div class="setting-row"><span class="setting-label">Size</span><span class="setting-value">${this.formatSize(video.sizeBytes)}</span></div>
                <div class="setting-row"><span class="setting-label">MP4 Compliant</span><span class="setting-value">${video.mp4Compliant ? '<span style="color:var(--success)">Yes</span>' : '<span style="color:var(--warning)">No</span>'}</span></div>
                ${video.needsOptimization ? `<div class="setting-row"><span class="setting-label">Needs Optimization</span><span class="setting-value"><span style="color:var(--warning)">Yes</span></span></div>` : ''}
                ${video.moovPosition ? `<div class="setting-row"><span class="setting-label">MOOV Position</span><span class="setting-value">${this.esc(video.moovPosition)}</span></div>` : ''}
                <div class="setting-row"><span class="setting-label">Added</span><span class="setting-value">${new Date(video.dateAdded).toLocaleString()}</span></div>
                ${video.lastPlayed ? `<div class="setting-row"><span class="setting-label">Last Played</span><span class="setting-value">${new Date(video.lastPlayed).toLocaleString()}</span></div>` : ''}
                ${video.needsOptimization ? `<div style="padding:8px 14px 6px;border-top:1px solid var(--border)"><button class="btn-primary" onclick="App.fixMp4(${video.id})" id="fix-mp4-btn">&#128295; Fix MP4 (Remux with FastStart)</button><span id="fix-mp4-status" style="margin-left:12px;font-size:13px;color:var(--text-secondary)"></span></div>` : ''}
            </div>
        </div>`;

        let html = `<div class="page-header" style="display:flex;justify-content:space-between;align-items:flex-start;gap:16px">
            <div>
                <button class="filter-chip" onclick="App.stopMvShuffle(); App.navigate('musicvideos')" style="margin-bottom:8px">&laquo; Back to Music Videos</button>
                <h1>${this.esc(video.title)}</h1>
                <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">
                    ${this.esc(video.artist)}${video.year ? ' &middot; ' + video.year : ''}
                    &middot; ${this.formatDuration(video.duration)} &middot; ${this.formatSize(video.sizeBytes)}
                </div>
            </div>
            ${mvActionsAndPopup}
        </div>`;

        // Shuffle bar (shown above player only when shuffle mode is active)
        if (this._mvShuffleMode) {
            html += `<div class="mv-shuffle-bar">
                <div class="mv-shuffle-bar-badge">
                    <svg><use href="#icon-shuffle"/></svg>
                    Shuffle Mode
                </div>
                <button class="mv-shuffle-next-btn" onclick="App._playNextShuffleMv()">
                    <svg><use href="#icon-shuffle"/></svg>
                    Next
                </button>
            </div>`;
        }

        // Video player
        html += `<div class="mv-player-container">
            <video id="mv-player" controls autoplay class="mv-player">
                <source src="/api/stream-musicvideo/${video.id}" type="video/mp4">
                Your browser does not support the video tag.
            </video>
        </div>`;

        el.innerHTML = html;

        // Connect EQ to the music video player
        const player = document.getElementById('mv-player');
        if (player) {
            this.connectEQToElement(player);
            player.addEventListener('ended', () => {
                if (this._mvShuffleMode) this._playNextShuffleMv();
            });
        }
    },

    playMusicVideo(id) {
        this.openMvDetail(id);
    },

    toggleMvDetails() {
        const arrow = document.getElementById('mv-details-arrow');
        const body = document.getElementById('mv-details-body');
        if (arrow) arrow.classList.toggle('collapsed');
        if (body) body.classList.toggle('collapsed');
    },

    async shuffleMusicVideo() {
        this._mvShuffleMode = true;
        const url = this.mvArtist
            ? `musicvideos/random?artist=${encodeURIComponent(this.mvArtist)}`
            : 'musicvideos/random';
        const result = await this.api(url);
        if (result && result.id) this.openMvDetail(result.id);
    },

    stopMvShuffle() {
        this._mvShuffleMode = false;
    },

    async _playNextShuffleMv() {
        if (!this._mvShuffleMode) return;
        const url = this.mvArtist
            ? `musicvideos/random?artist=${encodeURIComponent(this.mvArtist)}`
            : 'musicvideos/random';
        const result = await this.api(url);
        if (result && result.id) {
            this.openMvDetail(result.id);
        } else {
            this._mvShuffleMode = false;
        }
    },

    async fixMp4(id) {
        const btn = document.getElementById('fix-mp4-btn');
        const status = document.getElementById('fix-mp4-status');
        if (btn) btn.disabled = true;
        if (status) status.textContent = 'Remuxing...';
        const result = await this.apiPost(`musicvideo/fix-mp4/${id}`);
        if (status) status.textContent = result?.message || 'Done';
        if (btn) btn.disabled = false;
    },

    // ─── eBooks Library ─────────────────────────────
    async renderEBooks(el) {
        this.ebooksPage = 1;
        this.ebooksSort = 'recent';
        this.ebooksCategory = null;
        this.ebooksFormat = null;
        await this.loadEBooksPage(el);
    },

    async loadEBooksPage(el) {
        const target = el || document.getElementById('main-content');
        let url = `ebooks?limit=${this.ebooksPerPage}&page=${this.ebooksPage}&sort=${this.ebooksSort}`;
        if (this.ebooksCategory) url += `&category=${encodeURIComponent(this.ebooksCategory)}`;
        if (this.ebooksFormat) url += `&format=${encodeURIComponent(this.ebooksFormat)}`;

        const [data, categories] = await Promise.all([
            this.api(url),
            this.api('ebooks/categories')
        ]);

        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load eBooks.'); return; }
        this.ebooksTotal = data.total;
        const totalPages = Math.ceil(data.total / this.ebooksPerPage);

        let html = `<div class="page-header"><h1>${this.t('page.ebooks')}</h1>
            <div class="filter-bar">`;
        const sortLabels = { recent: this.t('sort.recent'), title: this.t('sort.title'), author: this.t('sort.author'), name: this.t('sort.filename'), size: this.t('sort.size') };
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.ebooksSort === key ? ' active' : ''}" onclick="App.changeEBooksSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

        // Format filter chips
        html += '<div style="display:flex;flex-wrap:wrap;gap:8px;margin-bottom:12px">';
        [{ key: null, label: 'All' }, { key: 'PDF', label: 'PDF' }, { key: 'EPUB', label: 'EPUB' }, { key: 'comic', label: '&#128366; Comic Books' }].forEach(f => {
            html += `<button class="filter-chip${this.ebooksFormat === f.key ? ' active' : ''}" onclick="App.filterEBookFormat(${f.key ? `'${f.key}'` : 'null'})">${f.label}</button>`;
        });
        html += '</div>';

        // Category filter chips
        if (categories && categories.length > 0) {
            html += '<div style="display:flex;flex-wrap:wrap;gap:8px;margin-bottom:16px">';
            html += `<button class="filter-chip${!this.ebooksCategory ? ' active' : ''}" onclick="App.filterEBookCategory(null)">${this.t('filter.allEbooks')}</button>`;
            categories.forEach(c => {
                const isActive = this.ebooksCategory === c.name;
                const displayName = c.name || 'Uncategorized';
                html += `<button class="filter-chip${isActive ? ' active' : ''}" onclick="App.filterEBookCategory('${this.esc(c.name)}')">${this.esc(displayName)} <span style="opacity:.6">${c.count}</span></button>`;
            });
            html += '</div>';
        }

        if (data.ebooks && data.ebooks.length > 0) {
            html += `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;color:var(--text-secondary);font-size:13px">
                <span>${data.total} eBooks${this.ebooksCategory ? ' in ' + this.esc(this.ebooksCategory || 'Uncategorized') : ''}</span>
                <span>Page ${this.ebooksPage} of ${totalPages}</span>
            </div>`;

            html += '<div class="ebooks-grid">';
            data.ebooks.forEach(book => {
                const formatBadge = book.format ? book.format.toLowerCase() : 'epub';
                const author = book.author || 'Unknown Author';
                const pages = book.pageCount > 0 ? `${book.pageCount} pages` : book.format;
                html += `<div class="ebook-card" onclick="App.openEBookDetail(${book.id})" data-ebook-id="${book.id}">
                    <div class="ebook-card-cover">
                        ${book.coverImage
                            ? `<img src="/ebookcover/${book.coverImage}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="ebook-card-placeholder" style="display:none">&#128214;</span>`
                            : `<span class="ebook-card-placeholder">&#128214;</span>`}
                        <span class="ebook-format-badge ebook-format-${formatBadge}">${book.format}</span>
                    </div>
                    <div class="ebook-card-info">
                        <div class="ebook-card-title">${this.esc(book.title)}</div>
                        <div class="ebook-card-author">${this.esc(author)}</div>
                        <div class="ebook-card-meta">
                            <span>${pages}</span>
                            <span>${this.formatSize(book.fileSize)}</span>
                        </div>
                    </div>
                </div>`;
            });
            html += '</div>';
            html += this.renderEBooksPagination(this.ebooksPage, totalPages);
        } else {
            const hasFilters = this.ebooksCategory || this.ebooksFormat;
            const emptyTitle = hasFilters ? 'No eBooks found' : this.t('empty.noEbooks.title');
            const emptyDesc  = hasFilters
                ? `No eBooks match the selected filters. <a href="#" onclick="App.renderEBooks()" style="color:var(--accent)">Clear all filters</a>`
                : this.t('empty.noEbooks.desc');
            html += `<div class="empty-state">
                <div class="empty-icon"><svg style="width:52px;height:52px;stroke:var(--text-muted);fill:none;stroke-width:1.2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-book"/></svg></div>
                <h2>${emptyTitle}</h2><p>${emptyDesc}</p>
            </div>`;
        }

        target.innerHTML = html;
    },

    changeEBooksSort(sort) {
        this.ebooksSort = sort;
        this.ebooksPage = 1;
        this.loadEBooksPage();
    },

    filterEBookCategory(category) {
        this.ebooksCategory = category;
        this.ebooksFormat = null;   // reset format so the two rows never silently stack
        this.ebooksPage = 1;
        this.loadEBooksPage();
    },

    filterEBookFormat(format) {
        this.ebooksFormat = format;
        this.ebooksCategory = null; // reset genre so the two rows never silently stack
        this.ebooksPage = 1;
        this.loadEBooksPage();
    },

    goEBooksPage(page) {
        const totalPages = Math.ceil(this.ebooksTotal / this.ebooksPerPage);
        if (page < 1 || page > totalPages) return;
        this.ebooksPage = page;
        this.loadEBooksPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    renderEBooksPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goEBooksPage(${currentPage - 1})">&laquo; ${this.t('pagination.prev')}</button>`;
        const pages = [];
        pages.push(1);
        let rangeStart = Math.max(2, currentPage - 2);
        let rangeEnd = Math.min(totalPages - 1, currentPage + 2);
        if (rangeStart > 2) pages.push('...');
        for (let p = rangeStart; p <= rangeEnd; p++) pages.push(p);
        if (rangeEnd < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goEBooksPage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goEBooksPage(${currentPage + 1})">${this.t('pagination.next')} &raquo;</button>`;
        html += '</div>';
        return html;
    },

    async openEBookDetail(ebookId) {
        const ebook = await this.api(`ebooks/${ebookId}`);
        if (!ebook) return;

        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(ebook.title)}</span>`;

        const formatBadge = ebook.format ? ebook.format.toLowerCase() : 'epub';
        const isComic = ebook.format === 'CBZ' || ebook.format === 'CBR';
        const readLabel = isComic ? '&#128366; Read Comic' : `&#128214; Read ${ebook.format}`;
        let html = `<div class="page-header">
            <div><h1>${this.esc(ebook.title)}</h1>
                <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">
                    ${this.esc(ebook.author || 'Unknown Author')}
                    ${ebook.pageCount > 0 ? ' &middot; ' + ebook.pageCount + ' pages' : ''}
                    &middot; ${this.formatSize(ebook.fileSize)}
                </div>
            </div>
            <div style="display:flex;gap:10px;align-items:center;flex-shrink:0">
                <button class="btn-primary" onclick="App.openEBookReader(${ebook.id})">${readLabel}</button>
                <a href="/api/ebooks/${ebook.id}/download" class="btn-primary" style="text-decoration:none;background:var(--bg-surface);color:var(--text-secondary)" target="_blank">&#128229; Download</a>
                <button class="btn-primary" style="background:var(--bg-surface);color:var(--text-secondary)" onclick="App.openEBookMetadataEditor(${ebook.id})" title="Edit Metadata">
                    <svg style="width:15px;height:15px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;vertical-align:middle;margin-right:4px"><use href="#icon-tool"/></svg>Edit
                </button>
            </div>
        </div>`;

        if (ebook.coverImage) {
            html += `<div style="margin-bottom:20px;text-align:center">
                <img src="/ebookcover/${ebook.coverImage}" style="max-width:300px;max-height:450px;border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,.3)" onerror="this.style.display='none'" alt="Cover">
            </div>`;
        }

        // Community Rating
        const ebookRatingSum = await this.api(`ratings/summary/ebook/${ebookId}`);
        html += `<div class="settings-section">
            <div class="setting-row">
                <span class="setting-label">Community Rating</span>
                <span class="setting-value">${this.buildRatingWidget(ebookRatingSum, 'ebook', ebookId)}</span>
            </div>
        </div>`;

        html += '<div class="settings-section">';
        html += '<h3>eBook Details</h3>';
        html += `<div class="setting-row setting-row-col"><span class="setting-label">Filename</span><span class="setting-value" style="word-break:break-all;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;line-height:1.4">${this.esc(ebook.fileName)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Format</span><span class="setting-value"><span class="ebook-format-badge ebook-format-${formatBadge}" style="position:static;font-size:11px">${ebook.format}</span></span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Size</span><span class="setting-value">${this.formatSize(ebook.fileSize)}</span></div>`;
        if (ebook.pageCount > 0)
            html += `<div class="setting-row"><span class="setting-label">Pages</span><span class="setting-value">${ebook.pageCount}</span></div>`;
        if (ebook.author)
            html += `<div class="setting-row"><span class="setting-label">Author</span><span class="setting-value">${this.esc(ebook.author)}</span></div>`;
        if (ebook.publisher)
            html += `<div class="setting-row"><span class="setting-label">Publisher</span><span class="setting-value">${this.esc(ebook.publisher)}</span></div>`;
        if (ebook.language)
            html += `<div class="setting-row"><span class="setting-label">Language</span><span class="setting-value">${this.esc(ebook.language)}</span></div>`;
        if (ebook.isbn)
            html += `<div class="setting-row"><span class="setting-label">ISBN</span><span class="setting-value">${this.esc(ebook.isbn)}</span></div>`;
        if (ebook.subject)
            html += `<div class="setting-row"><span class="setting-label">Subject</span><span class="setting-value">${this.esc(ebook.subject)}</span></div>`;
        if (ebook.category)
            html += `<div class="setting-row"><span class="setting-label">Category</span><span class="setting-value">${this.esc(ebook.category)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Added</span><span class="setting-value">${new Date(ebook.dateAdded).toLocaleString()}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">File Modified</span><span class="setting-value">${new Date(ebook.lastModified).toLocaleString()}</span></div>`;
        if (ebook.description) {
            html += `<div style="margin-top:12px;padding-top:12px;border-top:1px solid rgba(255,255,255,.03)">
                <div style="font-size:12px;color:var(--text-muted);margin-bottom:6px">Description</div>
                <div style="font-size:13px;color:var(--text-secondary);line-height:1.6">${this.esc(ebook.description)}</div>
            </div>`;
        }
        html += '</div>';

        el.innerHTML = html;
    },

    // ─── eBook Metadata Editor ───────────────────────────
    _ebookGenres: [
        'Information Technology','Medicine & Health','Science','History',
        'Business & Finance','Self-Help & Psychology','Literature & Fiction',
        'Law & Politics','Art & Design','Music','Cooking & Food',
        'Travel & Geography','Religion & Philosophy','Comics & Manga',
        'Paranormal & UFO','General'
    ],

    async openEBookMetadataEditor(ebookId) {
        const ebook = await this.api(`ebooks/${ebookId}`);
        if (!ebook) return;

        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>Edit: ${this.esc(ebook.title)}</span>`;

        const genreOptions = this._ebookGenres.map(g =>
            `<option value="${this.esc(g)}"${ebook.category === g ? ' selected' : ''}>${this.esc(g)}</option>`
        ).join('');

        el.innerHTML = `
        <div class="page-header">
            <div><h1>Edit Metadata</h1>
                <div style="color:var(--text-secondary);font-size:13px;margin-top:4px">${this.esc(ebook.fileName)}</div>
            </div>
            <div style="display:flex;gap:10px;align-items:center">
                <button class="btn-primary" onclick="App.saveEBookMetadata(${ebook.id})">&#10003; Save</button>
                <button class="btn-primary" style="background:var(--bg-surface);color:var(--text-secondary)" onclick="App.openEBookDetail(${ebook.id})">Cancel</button>
            </div>
        </div>
        <div class="settings-section">
            <h3>eBook Metadata</h3>
            <div class="video-edit-form-group">
                <label class="video-edit-label">Title</label>
                <input class="video-edit-input" id="eb-edit-title" value="${this.esc(ebook.title)}" placeholder="Book title">
            </div>
            <div class="video-edit-form-group">
                <label class="video-edit-label">Author</label>
                <input class="video-edit-input" id="eb-edit-author" value="${this.esc(ebook.author || '')}" placeholder="Author name">
            </div>
            <div class="video-edit-form-group">
                <label class="video-edit-label">Genre / Category</label>
                <select class="video-edit-select" id="eb-edit-genre">
                    ${genreOptions}
                </select>
            </div>
            <div class="video-edit-form-group">
                <label class="video-edit-label">Description</label>
                <textarea class="video-edit-input" id="eb-edit-desc" rows="4" style="resize:vertical;font-family:inherit">${this.esc(ebook.description || '')}</textarea>
            </div>
        </div>
        <div class="settings-section" style="color:var(--text-muted);font-size:12px">
            <strong>Note:</strong> Genre changes take effect immediately. To prevent the scanner from overwriting your manual genre assignment on the next rescan,
            the genre is considered user-confirmed once saved here and will not be auto-reclassified.
        </div>`;
    },

    async saveEBookMetadata(ebookId) {
        const title  = document.getElementById('eb-edit-title')?.value?.trim();
        const author = document.getElementById('eb-edit-author')?.value?.trim();
        const genre  = document.getElementById('eb-edit-genre')?.value;
        const desc   = document.getElementById('eb-edit-desc')?.value?.trim();

        if (!title) { alert('Title cannot be empty.'); return; }

        const res = await this.apiPut(`ebooks/${ebookId}`, { title, author, genre, description: desc });
        if (res?.success) {
            await this.openEBookDetail(ebookId);
        } else {
            alert('Save failed. Please try again.');
        }
    },

    // ─── eBook Reader ────────────────────────────────────
    _epubBook: null,
    _epubRendition: null,
    _ebookReaderOpen: false,
    _ebookPopstateHandler: null,

    async openEBookReader(ebookId) {
        const ebook = await this.api(`ebooks/${ebookId}`);
        if (!ebook) return;

        const fmt = ebook.format.toUpperCase();
        const isPdf   = fmt === 'PDF';
        const isComic = fmt === 'CBZ' || fmt === 'CBR';

        let html = `<div class="ebook-reader-overlay" id="ebook-reader-overlay">
            <div class="ebook-reader-toolbar">
                <button class="ebook-reader-btn" onclick="App.closeEBookReader()">
                    <svg><use href="#icon-chevron-left"/></svg> Back
                </button>
                <span class="ebook-reader-title">${this.esc(ebook.title)}</span>
                <div class="ebook-reader-actions">`;

        if (isComic) {
            html += `<button class="ebook-reader-btn" onclick="App.comicPrev()" title="Previous page">&lsaquo;</button>
                <input type="number" class="epub-page-input" id="comic-page-input" min="1" value="1"
                    onchange="App.comicGoToPage(this.value)" onkeydown="if(event.key==='Enter'){App.comicGoToPage(this.value);this.blur();}">
                <span class="epub-page-total" id="comic-page-total">/ ?</span>
                <button class="ebook-reader-btn" onclick="App.comicNext()" title="Next page">&rsaquo;</button>`;
        } else if (!isPdf) {
            html += `<span class="epub-page-nav" id="epub-page-nav" style="display:none">
                    <button class="ebook-reader-btn" onclick="App.epubPrev()" title="Previous page">&lsaquo;</button>
                    <input type="number" class="epub-page-input" id="epub-page-input" min="1" value="1"
                        onchange="App.epubGoToPage(this.value)" onkeydown="if(event.key==='Enter'){App.epubGoToPage(this.value);this.blur();}">
                    <span class="epub-page-total" id="epub-page-total">/ ?</span>
                    <button class="ebook-reader-btn" onclick="App.epubNext()" title="Next page">&rsaquo;</button>
                </span>
                <span class="epub-progress" id="epub-progress"></span>
                <button class="ebook-reader-btn" id="epub-font-dec" title="Decrease font" onclick="App.epubFontSize(-2)">A-</button>
                <button class="ebook-reader-btn" id="epub-font-inc" title="Increase font" onclick="App.epubFontSize(2)">A+</button>`;
        }

        html += `    <a href="/api/ebooks/${ebook.id}/download" class="ebook-reader-btn" title="Download" target="_blank">
                        <svg><use href="#icon-download"/></svg>
                    </a>
                </div>
            </div>
            <div class="ebook-reader-body">`;

        if (isPdf) {
            html += `<div class="ebook-reader-content">
                <iframe src="/api/ebooks/${ebook.id}/view" class="ebook-pdf-frame" id="ebook-pdf-frame"></iframe>
            </div>`;
        } else if (isComic) {
            html += `<div class="ebook-reader-content comic-reader-content" id="comic-reader-area"
                    ontouchstart="App._comicTouchStart(event)" ontouchend="App._comicTouchEnd(event)">
                <button class="epub-nav-btn epub-nav-prev" onclick="App.comicPrev()">&lsaquo;</button>
                <img id="comic-page-img" src="/api/ebooks/${ebook.id}/page/0"
                    style="max-width:100%;max-height:100%;object-fit:contain;display:block;margin:auto"
                    alt="Page 1">
                <button class="epub-nav-btn epub-nav-next" onclick="App.comicNext()">&rsaquo;</button>
            </div>`;
        } else {
            html += `<div class="ebook-reader-content" id="epub-reader-area">
                <button class="epub-nav-btn epub-nav-prev" onclick="App.epubPrev()">&lsaquo;</button>
                <div id="epub-viewer" style="width:100%;height:100%"></div>
                <button class="epub-nav-btn epub-nav-next" onclick="App.epubNext()">&rsaquo;</button>
            </div>`;
        }

        html += '</div></div>';

        document.body.insertAdjacentHTML('beforeend', html);

        if (isComic) {
            this._initComicReader(ebook.id);
        } else if (!isPdf) {
            this._initEpubReader(ebook.id);
        }

        // Push history state so browser back closes the reader
        this._ebookReaderOpen = true;
        history.pushState({ ebookReader: true }, '');
        if (this._ebookPopstateHandler) window.removeEventListener('popstate', this._ebookPopstateHandler);
        this._ebookPopstateHandler = (e) => {
            if (this._ebookReaderOpen) this.closeEBookReader(true);
        };
        window.addEventListener('popstate', this._ebookPopstateHandler);

        // Keyboard navigation
        this._ebookReaderKeyHandler = (e) => {
            if (e.key === 'Escape') this.closeEBookReader();
            if (isComic) {
                if (e.key === 'ArrowLeft') this.comicPrev();
                if (e.key === 'ArrowRight') this.comicNext();
            } else if (!isPdf) {
                if (e.key === 'ArrowLeft') this.epubPrev();
                if (e.key === 'ArrowRight') this.epubNext();
            }
        };
        document.addEventListener('keydown', this._ebookReaderKeyHandler);
    },

    // ─── Comic Reader ─────────────────────────────────────────────────
    _comicEbookId: null,
    _comicPage: 0,
    _comicTotal: 0,
    _comicTouchX: 0,

    async _initComicReader(ebookId) {
        this._comicEbookId = ebookId;
        this._comicPage = 0;
        const data = await this.api(`ebooks/${ebookId}/pagecount`);
        this._comicTotal = data?.pageCount || 1;
        const input = document.getElementById('comic-page-input');
        if (input) input.max = this._comicTotal;
        this._updateComicLabel();
    },

    _updateComicLabel() {
        const input = document.getElementById('comic-page-input');
        const total = document.getElementById('comic-page-total');
        if (input) input.value = this._comicPage + 1;
        if (total) total.textContent = '/ ' + this._comicTotal;
    },

    comicGoToPage(pageNum) {
        const page = parseInt(pageNum);
        if (isNaN(page)) return;
        const clamped = Math.max(1, Math.min(this._comicTotal, page));
        this._comicPage = clamped - 1;
        this._loadComicPage(this._comicPage);
    },

    _loadComicPage(page) {
        const img = document.getElementById('comic-page-img');
        if (!img) return;
        img.src = `/api/ebooks/${this._comicEbookId}/page/${page}`;
        img.alt = `Page ${page + 1}`;
        this._updateComicLabel();
    },

    comicNext() {
        if (this._comicPage < this._comicTotal - 1) {
            this._comicPage++;
            this._loadComicPage(this._comicPage);
        }
    },

    comicPrev() {
        if (this._comicPage > 0) {
            this._comicPage--;
            this._loadComicPage(this._comicPage);
        }
    },

    _comicTouchStart(e) { this._comicTouchX = e.changedTouches[0].clientX; },
    _comicTouchEnd(e) {
        const dx = e.changedTouches[0].clientX - this._comicTouchX;
        if (Math.abs(dx) > 50) { dx < 0 ? this.comicNext() : this.comicPrev(); }
    },

    _epubTotalPages: 0,

    async _initEpubReader(ebookId) {
        const viewer = document.getElementById('epub-viewer');
        try {
            if (this._epubBook) this._epubBook.destroy();
            // Fetch EPUB as ArrayBuffer so epub.js can parse it directly
            const response = await fetch(`/api/ebooks/${ebookId}/view`);
            if (!response.ok) throw new Error('Failed to fetch EPUB');
            const epubData = await response.arrayBuffer();

            this._epubBook = ePub(epubData);
            this._epubRendition = this._epubBook.renderTo('epub-viewer', {
                width: '100%',
                height: '100%',
                spread: 'auto',
                allowScriptedContent: true
            });
            this._epubRendition.themes.default({
                'body': { 'font-family': 'Georgia, serif', 'line-height': '1.7', 'padding': '20px' },
                'p': { 'margin-bottom': '0.8em' }
            });

            // Suppress CSS 404s: replace broken stylesheet links with empty content
            this._epubRendition.hooks.content.register((contents) => {
                const doc = contents.document;
                if (doc) {
                    doc.querySelectorAll('link[rel="stylesheet"]').forEach(link => {
                        if (link.href && !link.href.startsWith('blob:') && !link.href.startsWith('data:')) {
                            link.setAttribute('href', 'data:text/css,');
                        }
                    });
                }
            });

            this._epubRendition.display();
            this._epubBook.ready.then(() => {
                return this._epubBook.locations.generate(1024);
            }).then((locations) => {
                this._epubTotalPages = locations.length;
                const totalEl = document.getElementById('epub-page-total');
                const navEl = document.getElementById('epub-page-nav');
                const inputEl = document.getElementById('epub-page-input');
                if (totalEl) totalEl.textContent = '/ ' + this._epubTotalPages;
                if (inputEl) inputEl.max = this._epubTotalPages;
                if (navEl) navEl.style.display = '';

                this._epubRendition.on('relocated', (location) => {
                    const progress = document.getElementById('epub-progress');
                    const pageInput = document.getElementById('epub-page-input');
                    if (location.start) {
                        if (progress && location.start.percentage != null) {
                            progress.textContent = Math.round(location.start.percentage * 100) + '%';
                        }
                        // Update page number from location index
                        const currentPage = location.start.location + 1;
                        if (pageInput && !pageInput.matches(':focus')) {
                            pageInput.value = currentPage;
                        }
                    }
                });
            });
            // Allow keyboard navigation inside epub iframe
            this._epubRendition.on('keydown', (e) => {
                if (e.key === 'ArrowLeft') this.epubPrev();
                if (e.key === 'ArrowRight') this.epubNext();
                if (e.key === 'Escape') this.closeEBookReader();
            });
        } catch (err) {
            console.error('EPUB init error:', err);
            if (viewer) viewer.innerHTML = '<div style="padding:40px;text-align:center;color:var(--text-muted)">Could not load EPUB. Try downloading the file instead.</div>';
        }
    },

    epubNext() {
        if (this._epubRendition) this._epubRendition.next();
    },

    epubPrev() {
        if (this._epubRendition) this._epubRendition.prev();
    },

    epubGoToPage(pageNum) {
        const page = parseInt(pageNum);
        if (!page || !this._epubBook || !this._epubTotalPages) return;
        const clamped = Math.max(1, Math.min(this._epubTotalPages, page));
        const cfi = this._epubBook.locations.cfiFromLocation(clamped - 1);
        if (cfi && this._epubRendition) this._epubRendition.display(cfi);
    },

    _epubFontSizePx: 16,

    epubFontSize(delta) {
        this._epubFontSizePx = Math.max(10, Math.min(32, this._epubFontSizePx + delta));
        if (this._epubRendition) {
            this._epubRendition.themes.fontSize(this._epubFontSizePx + 'px');
        }
    },

    closeEBookReader(fromPopstate) {
        if (!this._ebookReaderOpen) return;
        this._ebookReaderOpen = false;
        if (this._epubBook) {
            this._epubBook.destroy();
            this._epubBook = null;
            this._epubRendition = null;
        }
        document.getElementById('ebook-reader-overlay')?.remove();
        if (this._ebookReaderKeyHandler) {
            document.removeEventListener('keydown', this._ebookReaderKeyHandler);
            this._ebookReaderKeyHandler = null;
        }
        if (this._ebookPopstateHandler) {
            window.removeEventListener('popstate', this._ebookPopstateHandler);
            this._ebookPopstateHandler = null;
        }
        // Pop the history entry we pushed, unless we got here from popstate (already popped)
        if (!fromPopstate) history.back();
    },

    // ─── Audio Books ──────────────────────────────────────
    async renderAudioBooks(el) {
        this.audioBooksPage = 1;
        this.audioBooksSort = 'recent';
        this.audioBooksCategory = null;
        await this.loadAudioBooksPage(el);
    },

    async loadAudioBooksPage(el) {
        const target = el || document.getElementById('main-content');
        let url = `audiobooks?limit=${this.audioBooksPerPage}&page=${this.audioBooksPage}&sort=${this.audioBooksSort}`;
        if (this.audioBooksCategory) url += `&category=${encodeURIComponent(this.audioBooksCategory)}`;

        const [data, categories] = await Promise.all([
            this.api(url),
            this.api('audiobooks/categories')
        ]);

        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load Audio Books.'); return; }
        this.audioBooksTotal = data.total;
        const totalPages = Math.ceil(data.total / this.audioBooksPerPage);

        let html = `<div class="page-header"><h1>${this.t('page.audioBooks')}</h1>
            <div class="filter-bar">`;
        const sortLabels = { recent: this.t('sort.recent'), title: this.t('sort.title'), author: this.t('sort.author'), name: this.t('sort.filename'), size: this.t('sort.size') };
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.audioBooksSort === key ? ' active' : ''}" onclick="App.changeAudioBooksSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

        if (categories && categories.length > 0) {
            html += '<div style="display:flex;flex-wrap:wrap;gap:8px;margin-bottom:16px">';
            html += `<button class="filter-chip${!this.audioBooksCategory ? ' active' : ''}" onclick="App.filterAudioBookCategory(null)">${this.t('filter.allAudioBooks')}</button>`;
            categories.forEach(c => {
                const isActive = this.audioBooksCategory === c.name;
                const displayName = c.name || 'Uncategorized';
                html += `<button class="filter-chip${isActive ? ' active' : ''}" onclick="App.filterAudioBookCategory('${this.esc(c.name)}')">${this.esc(displayName)} <span style="opacity:.6">${c.count}</span></button>`;
            });
            html += '</div>';
        }

        if (data.audioBooks && data.audioBooks.length > 0) {
            html += `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;color:var(--text-secondary);font-size:13px">
                <span>${data.total} ${this.t('misc.audioBooks')}${this.audioBooksCategory ? ' in ' + this.esc(this.audioBooksCategory || 'Uncategorized') : ''}</span>
                <span>Page ${this.audioBooksPage} of ${totalPages}</span>
            </div>`;

            html += '<div class="audiobooks-grid">';
            data.audioBooks.forEach(book => {
                const fmt = (book.format || 'MP3').toUpperCase();
                const fmtClass = fmt === 'M4B' ? 'm4b' : 'mp3';
                const author = book.author || 'Unknown Author';
                const dur = book.duration > 0 ? this.formatDuration(Math.round(book.duration)) : fmt;
                html += `<div class="ebook-card audiobook-card" onclick="App.openAudioBookDetail(${book.id})" data-audiobook-id="${book.id}">
                    <div class="ebook-card-cover">
                        ${book.coverImage
                            ? `<img src="/audiobookcover/${book.coverImage}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="ebook-card-placeholder" style="display:none">&#127911;</span>`
                            : `<span class="ebook-card-placeholder">&#127911;</span>`}
                        <span class="ebook-format-badge ebook-format-${fmtClass}">${fmt}</span>
                    </div>
                    <div class="ebook-card-info">
                        <div class="ebook-card-title">${this.esc(book.title)}</div>
                        <div class="ebook-card-author">${this.esc(author)}</div>
                        <div class="ebook-card-meta">
                            <span>${dur}</span>
                            <span>${this.formatSize(book.fileSize)}</span>
                        </div>
                    </div>
                    <button class="song-card-fav${book.isFavourite ? ' active' : ''}" onclick="event.stopPropagation(); App.toggleAudioBookFav(${book.id}, this)">&#10084;</button>
                </div>`;
            });
            html += '</div>';
            html += this.renderAudioBooksPagination(this.audioBooksPage, totalPages);
        } else {
            html += this.emptyState(this.t('empty.noAudioBooks.title'), this.t('empty.noAudioBooks.desc'));
        }

        target.innerHTML = html;
    },

    changeAudioBooksSort(sort) {
        this.audioBooksSort = sort;
        this.audioBooksPage = 1;
        this.loadAudioBooksPage();
    },

    filterAudioBookCategory(category) {
        this.audioBooksCategory = category;
        this.audioBooksPage = 1;
        this.loadAudioBooksPage();
    },

    goAudioBooksPage(page) {
        const totalPages = Math.ceil(this.audioBooksTotal / this.audioBooksPerPage);
        if (page < 1 || page > totalPages) return;
        this.audioBooksPage = page;
        this.loadAudioBooksPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    renderAudioBooksPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goAudioBooksPage(${currentPage - 1})">&laquo; ${this.t('pagination.prev')}</button>`;
        const pages = [];
        pages.push(1);
        let rangeStart = Math.max(2, currentPage - 2);
        let rangeEnd = Math.min(totalPages - 1, currentPage + 2);
        if (rangeStart > 2) pages.push('...');
        for (let p = rangeStart; p <= rangeEnd; p++) pages.push(p);
        if (rangeEnd < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goAudioBooksPage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goAudioBooksPage(${currentPage + 1})">${this.t('pagination.next')} &raquo;</button>`;
        html += '</div>';
        return html;
    },

    async openAudioBookDetail(audiobookId) {
        const book = await this.api(`audiobooks/${audiobookId}`);
        if (!book) return;

        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(book.title)}</span>`;

        const fmt = (book.format || 'MP3').toUpperCase();
        const fmtClass = fmt === 'M4B' ? 'm4b' : 'mp3';
        const dur = book.duration > 0 ? this.formatDuration(Math.round(book.duration)) : '';

        let html = `<div class="page-header">
            <div><h1>${this.esc(book.title)}</h1>
                <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">
                    ${this.esc(book.author || 'Unknown Author')}
                    ${dur ? ' &middot; ' + dur : ''}
                    &middot; ${this.formatSize(book.fileSize)}
                </div>
            </div>
            <div style="display:flex;gap:10px;align-items:center;flex-shrink:0">
                <button class="btn-primary" onclick="App.playAudioBook(${book.id}, '${this.esc(book.title)}', '${this.esc(book.author || '')}', ${!!book.isFavourite})">&#9654; ${this.t('btn.playAudioBook')}</button>
                <button class="song-card-fav${book.isFavourite ? ' active' : ''}" style="position:static;font-size:18px" onclick="App.toggleAudioBookFav(${book.id}, this)" title="${this.t('btn.favourite', 'Favorite')}">&#10084;</button>
                <a href="/api/audiobooks/${book.id}/download" class="btn-primary" style="text-decoration:none;background:var(--bg-surface);color:var(--text-secondary)" target="_blank">&#128229; Download</a>
            </div>
        </div>`;

        if (book.coverImage) {
            html += `<div style="margin-bottom:20px;text-align:center">
                <img src="/audiobookcover/${book.coverImage}" style="max-width:300px;max-height:300px;border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,.3)" onerror="this.style.display='none'" alt="Cover">
            </div>`;
        }

        html += '<div class="settings-section">';
        html += `<h3>${this.t('page.audioBooks')}</h3>`;
        html += `<div class="setting-row setting-row-col"><span class="setting-label">Filename</span><span class="setting-value" style="word-break:break-all;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;line-height:1.4">${this.esc(book.fileName)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Format</span><span class="setting-value"><span class="ebook-format-badge ebook-format-${fmtClass}" style="position:static;font-size:11px">${fmt}</span></span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Size</span><span class="setting-value">${this.formatSize(book.fileSize)}</span></div>`;
        if (dur) html += `<div class="setting-row"><span class="setting-label">${this.t('label.duration')}</span><span class="setting-value">${dur}</span></div>`;
        if (book.author) html += `<div class="setting-row"><span class="setting-label">${this.t('label.author')}</span><span class="setting-value">${this.esc(book.author)}</span></div>`;
        if (book.narrator) html += `<div class="setting-row"><span class="setting-label">${this.t('label.narrator')}</span><span class="setting-value">${this.esc(book.narrator)}</span></div>`;
        if (book.series) html += `<div class="setting-row"><span class="setting-label">${this.t('label.series')}</span><span class="setting-value">${this.esc(book.series)}</span></div>`;
        if (book.year) html += `<div class="setting-row"><span class="setting-label">${this.t('label.year')}</span><span class="setting-value">${book.year}</span></div>`;
        if (book.publisher) html += `<div class="setting-row"><span class="setting-label">${this.t('label.publisher')}</span><span class="setting-value">${this.esc(book.publisher)}</span></div>`;
        if (book.language) html += `<div class="setting-row"><span class="setting-label">${this.t('label.language')}</span><span class="setting-value">${this.esc(book.language)}</span></div>`;
        if (book.category) html += `<div class="setting-row"><span class="setting-label">${this.t('label.category')}</span><span class="setting-value">${this.esc(book.category)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('label.added')}</span><span class="setting-value">${new Date(book.dateAdded).toLocaleString()}</span></div>`;
        if (book.description) {
            html += `<div style="margin-top:12px;padding-top:12px;border-top:1px solid rgba(255,255,255,.03)">
                <div style="font-size:12px;color:var(--text-muted);margin-bottom:6px">${this.t('label.description')}</div>
                <div style="font-size:13px;color:var(--text-secondary);line-height:1.6">${this.esc(book.description)}</div>
            </div>`;
        }
        html += '</div>';

        el.innerHTML = html;
    },

    playAudioBook(id, title, author, isFavourite = false) {
        // Stop any playing video
        document.querySelectorAll('video').forEach(v => { v.pause(); v.removeAttribute('src'); v.load(); });
        // Clear playlist so prev/next don't step into unrelated tracks
        this.playlist = [];
        this.playIndex = -1;
        this.currentTrack = null;
        this.isRadioPlaying = false;
        this.isAudioBookPlaying = true;
        this._currentAudioBookId = id;
        document.getElementById('player-bar').classList.remove('radio-mode', 'player-hidden');
        // Hide controls that don't apply to a single audiobook file
        document.getElementById('btn-player-add-playlist').style.display = 'none';
        document.getElementById('btn-player-lyrics').style.display = 'none';
        document.getElementById('btn-shuffle').style.display = 'none';
        document.getElementById('btn-repeat').style.display = 'none';
        document.getElementById('btn-eq').style.display = 'none';
        document.getElementById('btn-prev').style.display = 'none';
        document.getElementById('btn-next').style.display = 'none';
        document.getElementById('progress-bar').style.display = '';
        // Show and configure the fav button
        const favBtn = document.getElementById('btn-player-fav');
        favBtn.style.display = '';
        favBtn.classList.toggle('active', !!isFavourite);
        this.audioPlayer.src = `/api/audiobooks/${id}/stream`;
        this.audioPlayer.play().catch(() => {});
        this.isPlaying = true;
        document.getElementById('btn-play').innerHTML = '<svg style="width:22px;height:22px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-pause"/></svg>';
        document.getElementById('player-title').textContent = title || this.t('misc.audioBook');
        document.getElementById('player-artist').textContent = author || '';
        document.getElementById('player-cover').innerHTML = '<div style="display:flex;align-items:center;justify-content:center;width:100%;height:100%;font-size:22px;color:#666">&#127911;</div>';
    },

    // ─── Movies ──────────────────────────────────────────────────
    async renderMovies(el) {
        this._currentVideoSection = 'movies';
        this.videosPage = 1;
        this.videosSort = 'recent';
        this.videosMediaType = null;
        this.videosGenre = null;
        this.videosCustomCategory = null;
        this.videosCustomGenreId = null;
        this._cgVideoPage = 0;
        this._videosView = 'all';
        await this.loadVideosPage(el);
    },

    // ─── TV Shows / Docs ─────────────────────────────────────────
    async renderTvShows(el) {
        this._currentVideoSection = 'tvshows';
        this.videosPage = 1;
        this.videosSort = 'recent';
        this.videosMediaType = null;
        this.videosGenre = null;
        this.videosCustomCategory = null;
        this.videosCustomGenreId = null;
        this._cgVideoPage = 0;
        this._videosView = 'all';
        await this.loadVideosPage(el);
    },

    async loadVideosPage(el) {
        const target = el || document.getElementById('main-content');
        const isTvSection = this._currentVideoSection === 'tvshows';

        // Determine the mediaType param for the API call
        let apiMediaType;
        if (isTvSection) {
            apiMediaType = this.videosMediaType || 'tv,documentary';
        } else {
            // Movies page always shows only movies
            apiMediaType = 'movie';
        }

        let url = `videos?limit=${this.videosPerPage}&page=${this.videosPage}&sort=${this.videosSort}&grouped=true&mediaType=${encodeURIComponent(apiMediaType)}`;
        if (this.videosGenre) url += `&genre=${encodeURIComponent(this.videosGenre)}`;
        if (this.videosCustomCategory) url += `&customCategory=${encodeURIComponent(this.videosCustomCategory)}`;
        if (this.videosCustomGenreId) url += `&customGenreId=${encodeURIComponent(this.videosCustomGenreId)}`;

        const genreUrl = `videos/genres?mediaType=${encodeURIComponent(apiMediaType)}`;
        const customCatUrl = `videos/custom-categories?mediaType=${encodeURIComponent(apiMediaType)}`;
        const cgDomain = isTvSection ? (this.videosMediaType === 'anime' ? 'anime' : 'tv') : 'movie';
        const customGenreUrl = `custom-genres?domain=${encodeURIComponent(cgDomain)}`;
        const [data, stats, genreData, customCatData, customGenreData] = await Promise.all([
            this.api(url),
            this.api('videos/stats'),
            this.api(genreUrl),
            this.api(customCatUrl),
            this.api(customGenreUrl)
        ]);

        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load videos.'); return; }
        this.videosTotal = data.total;
        const totalPages = Math.ceil(data.total / this.videosPerPage);

        const pageTitle = isTvSection ? this.t('page.tvShows') : this.t('page.movies');
        let html = `<div class="page-header"><h1>${pageTitle}</h1>
            <div class="filter-bar">`;
        const sortLabels = { recent: this.t('sort.recent'), title: this.t('sort.title'), year: this.t('sort.year'), size: this.t('sort.size'), duration: this.t('sort.duration'), series: this.t('sort.series') };
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.videosSort === key ? ' active' : ''}" onclick="App.changeVideosSort('${key}')">${label}</button>`;
        }
        html += `<button class="filter-chip batch-edit-chip${this._batchSelectMode ? ' active' : ''}" onclick="App._batchToggleSelect()" title="Select multiple items to edit in bulk"><svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 11 12 14 22 4"/><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/></svg>${this._batchSelectMode ? `Batch (${this._batchSelectedIds?.size || 0})` : 'Batch Edit'}</button>`;
        html += `</div></div>`;

        // Media type / section chips
        html += '<div style="display:flex;gap:8px;margin-bottom:16px;flex-wrap:wrap">';
        if (isTvSection) {
            // TV Shows/Docs: All | TV Shows | Documentaries
            html += `<button class="filter-chip${!this.videosMediaType ? ' active' : ''}" onclick="App.filterVideosType(null)">${this.t('filter.all')}</button>`;
            html += `<button class="filter-chip${this.videosMediaType === 'tv' ? ' active' : ''}" onclick="App.filterVideosType('tv')">${this.t('filter.tvShows')}</button>`;
            html += `<button class="filter-chip${this.videosMediaType === 'documentary' ? ' active' : ''}" onclick="App.filterVideosType('documentary')">${this.t('filter.documentaries')}</button>`;
        } else {
            // Movies: Collections tab only (page already filters to movies)
            html += `<button class="filter-chip${this._videosView === 'collections' ? ' active' : ''}" onclick="App.showCollectionsView()"><svg style="width:13px;height:13px;stroke:currentColor;fill:none;stroke-width:2;vertical-align:middle;margin-right:4px"><use href="#icon-film"/></svg>Collections</button>`;
        }
        html += '</div>';

        // Custom category chips row (folder-based user categories, e.g. "Kids Movies", "Christmas Music")
        const customCats = customCatData || [];
        if (customCats.length > 0) {
            html += '<div class="genre-bar custom-cat-bar">';
            html += `<span class="genre-bar-label">${this.t('filter.myFolders')}</span>`;
            html += `<button class="filter-chip${!this.videosCustomCategory ? ' active' : ''}" onclick="App.filterVideosCustomCategory(null)">${this.t('filter.allFolders')}</button>`;
            customCats.forEach(c => {
                const active = this.videosCustomCategory === c.name ? ' active' : '';
                html += `<button class="filter-chip${active}" onclick="App.filterVideosCustomCategory('${this.esc(c.name).replace(/'/g, "\\'")}')">
                    ${this.esc(c.name)}<span class="genre-count">${c.count}</span></button>`;
            });
            html += '</div>';
        }

        // Genre chips row
        const genres = genreData || [];
        if (genres.length > 0) {
            html += '<div class="genre-bar">';
            html += `<button class="filter-chip${!this.videosGenre ? ' active' : ''}" onclick="App.filterVideosGenre(null)">All Genres</button>`;
            genres.forEach(g => {
                const active = this.videosGenre === g.name ? ' active' : '';
                html += `<button class="filter-chip${active}" onclick="App.filterVideosGenre('${this.esc(g.name).replace(/'/g, "\\'")}')">
                    ${this.esc(g.name)}<span class="genre-count">${g.count}</span></button>`;
            });
            html += '</div>';
        }

        // Custom genre chips row
        const customGenreList = customGenreData || [];
        const CG_PER_PAGE = 10;
        const cgTotalPages = Math.ceil(customGenreList.length / CG_PER_PAGE);
        const cgPage = Math.min(this._cgVideoPage || 0, Math.max(0, cgTotalPages - 1));
        const cgSlice = customGenreList.slice(cgPage * CG_PER_PAGE, (cgPage + 1) * CG_PER_PAGE);
        html += '<div class="genre-bar custom-genre-bar">';
        html += `<span class="genre-bar-label">${this.t('customGenres.title')}</span>`;
        html += `<button class="filter-chip cg-add-btn" onclick="App.openCreateCustomGenreModal('${cgDomain}')">${this.t('customGenres.add')}</button>`;
        if (this.videosCustomGenreId) {
            html += `<button class="filter-chip active" onclick="App.filterVideosCustomGenre(null)" title="${this.t('filter.allGenres')}"><svg style="width:10px;height:10px;stroke:currentColor;fill:none;stroke-width:2.5;stroke-linecap:round"><use href="#icon-x"/></svg></button>`;
        }
        if (cgTotalPages > 1) {
            html += `<button class="cg-page-btn" onclick="App._cgVideoNav(-1)" ${cgPage === 0 ? 'disabled' : ''} title="Previous">&#8249;</button>`;
        }
        cgSlice.forEach(cg => {
            const active = this.videosCustomGenreId === cg.id ? ' active' : '';
            const safeId = this.esc(cg.id).replace(/'/g, "\\'");
            html += `<button class="filter-chip${active}" onclick="App.filterVideosCustomGenre('${safeId}')">
                ${this.esc(cg.name)}<span class="genre-count">${cg.count}</span>
                <span class="cg-edit-icon" onclick="event.stopPropagation(); App.openEditCustomGenreModal('${safeId}', '${cgDomain}')" title="${this.t('customGenres.edit')}"><svg style="width:11px;height:11px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-edit"/></svg></span>
            </button>`;
        });
        if (cgTotalPages > 1) {
            html += `<button class="cg-page-btn" onclick="App._cgVideoNav(1)" ${cgPage >= cgTotalPages - 1 ? 'disabled' : ''} title="Next">&#8250;</button>`;
            html += `<span class="cg-page-info">${cgPage + 1} / ${cgTotalPages}</span>`;
        }
        html += '</div>';

        // Stats bar (context-specific)
        if (stats) {
            if (isTvSection) {
                const tvItems = (stats.totalTvSeries || 0) + (stats.totalDocumentaries || 0);
                if (tvItems > 0) {
                    html += `<div class="mv-stats-bar">
                        ${stats.totalTvSeries > 0 ? `<span>${stats.totalTvSeries} series</span>` : ''}
                        <span>${stats.totalTvEpisodes} episodes</span>
                        ${stats.totalDocumentaries > 0 ? `<span>${stats.totalDocumentaries} documentaries</span>` : ''}
                        <span>${this.formatSize(stats.totalSize)}</span>
                        <span>${this.formatDuration(stats.totalDuration)}</span>
                        ${stats.needsOptimization > 0 ? `<span class="mv-stat-warn">${stats.needsOptimization} need optimization</span>` : ''}
                    </div>`;
                }
            } else {
                if (stats.totalMovies > 0) {
                    html += `<div class="mv-stats-bar">
                        <span>${stats.totalMovies} movies</span>
                        <span>${this.formatSize(stats.totalSize)}</span>
                        <span>${this.formatDuration(stats.totalDuration)}</span>
                        ${stats.needsOptimization > 0 ? `<span class="mv-stat-warn">${stats.needsOptimization} need optimization</span>` : ''}
                    </div>`;
                }
            }
        }

        if (data.videos && data.videos.length > 0) {
            if (totalPages > 1) {
                html += `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;color:var(--text-secondary);font-size:13px">
                    <span>${data.total} videos</span>
                    <span>Page ${this.videosPage} of ${totalPages}</span>
                </div>`;
            }

            html += '<div class="mv-grid">';
            const buildHdrBadge = hdr => {
                if (!hdr) return '';
                const cls = hdr === 'Dolby Vision' ? 'mv-hdr-dv'
                          : hdr === 'HDR10+' ? 'mv-hdr-plus'
                          : hdr === 'HLG' ? 'mv-hdr-hlg' : '';
                return `<span class="mv-hdr-badge ${cls}">${this.esc(hdr)}</span>`;
            };

            data.videos.forEach(v => {
                // Prefer poster > thumbnail > placeholder
                const thumbSrc = v.posterPath ? `/videometa/${v.posterPath}` : (v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '');
                const hasPoster = !!v.posterPath;
                const dur = this.formatDuration(v.duration);
                const resLabel = v.height >= 2160 ? '4K' : v.height >= 1080 ? '1080p' : v.height >= 720 ? '720p' : v.height > 0 ? v.height + 'p' : '';
                const hdrBadge = buildHdrBadge(v.hdrFormat || '');
                const ratingBadge = v.rating > 0 ? `<span class="mv-rating-badge" style="background:${v.rating >= 7 ? 'rgba(39,174,96,.85)' : v.rating >= 5 ? 'rgba(241,196,15,.85)' : 'rgba(231,76,60,.85)'}">${v.rating.toFixed(1)}</span>` : '';

                if (v.type === 'series') {
                    // TV Series grouped card
                    const epLabel = `${v.episodeCount} Episode${v.episodeCount !== 1 ? 's' : ''}`;
                    const seasonLabel = `${v.seasonCount} Season${v.seasonCount !== 1 ? 's' : ''}`;
                    html += `<div class="mv-card mv-card-series${hasPoster ? ' mv-card-poster' : ''}" onclick="App.openSeriesDetail('${this.esc(v.seriesName).replace(/'/g, "\\'")}')" data-series="${this.esc(v.seriesName)}">
                        <div class="mv-card-thumb${hasPoster ? ' mv-poster-thumb' : ''}">
                            ${thumbSrc
                                ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                                   <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`
                                : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`}
                            <span class="mv-duration-badge">${dur}</span>
                            ${resLabel ? `<span class="mv-format-badge mv-format-ok">${resLabel}</span>` : ''}
                            ${hdrBadge}
                            <span class="video-type-badge video-type-tv">TV</span>
                            <span class="mv-episode-badge">${v.episodeCount}</span>
                            ${ratingBadge}
                        </div>
                        <div class="mv-card-info">
                            <div class="mv-card-title">${this.esc(v.seriesName)}</div>
                            <div class="mv-card-artist">${seasonLabel} &middot; ${epLabel}</div>
                        </div>
                        <button class="mv-card-menu-btn" onclick="event.stopPropagation(); App.showVideoMenu(${v.firstEpisodeId || v.id}, 'tv', event)" title="More options">&#8942;</button>
                    </div>`;
                } else {
                    // Individual video card (movie or ungrouped episode)
                    const typeLabel = v.mediaType === 'tv' ? 'TV' : v.mediaType === 'documentary' ? 'Doc' : v.mediaType === 'anime' ? 'Anime' : 'Movie';
                    const subtitle = v.mediaType === 'tv' && v.seriesName
                        ? `${this.esc(v.seriesName)} S${(v.season||0).toString().padStart(2,'0')}E${(v.episode||0).toString().padStart(2,'0')}`
                        : (v.year ? v.year : '');
                    const vidFavClass = v.isFavourite ? 'active' : '';
                    const watchedBadge = v.isWatched ? `<span class="mv-watched-badge"><svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>WATCHED</span>` : '';
                    html += `<div class="mv-card${hasPoster ? ' mv-card-poster' : ''}${this._batchSelectMode && this._batchSelectedIds?.has(v.id) ? ' batch-selected' : ''}" onclick="App._batchSelectMode ? App._batchToggleCard(${v.id}, this) : App.openVideoDetail(${v.id})" data-video-id="${v.id}" data-watched="${v.isWatched ? '1' : '0'}">
                        ${this._batchSelectMode ? `<label class="batch-cb-wrap" onclick="event.stopPropagation()"><input type="checkbox" class="batch-cb" ${this._batchSelectedIds?.has(v.id) ? 'checked' : ''} onchange="App._batchToggleCard(${v.id}, this.closest('.mv-card'))"></label>` : ''}
                        <div class="mv-card-thumb${hasPoster ? ' mv-poster-thumb' : ''}">
                            ${thumbSrc
                                ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                                   <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`
                                : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`}
                            ${watchedBadge}
                            <span class="mv-duration-badge">${dur}</span>
                            ${resLabel ? `<span class="mv-format-badge mv-format-ok">${resLabel}</span>` : ''}
                            ${hdrBadge}
                            <span class="video-type-badge video-type-${v.mediaType}">${typeLabel}</span>
                            <button class="mv-card-play" onclick="event.stopPropagation(); App.playVideo(${v.id})">&#9654;</button>
                            ${ratingBadge}
                        </div>
                        <div class="mv-card-info">
                            <div class="mv-card-title">${this.esc(v.title)}</div>
                            <div class="mv-card-artist">${subtitle}</div>
                        </div>
                        <button class="song-card-fav ${vidFavClass}" onclick="event.stopPropagation(); App.toggleVideoFav(${v.id}, this)">&#10084;</button>
                        <button class="mv-card-menu-btn" onclick="event.stopPropagation(); App.showVideoMenu(${v.id}, '${v.mediaType}', event)" title="More options">&#8942;</button>
                    </div>`;
                }
            });
            html += '</div>';
            html += this.renderVideosPagination(this.videosPage, totalPages);
        } else {
            html += this.emptyState(this.t('empty.noVideos.title'), this.t('empty.noVideos.desc'));
        }

        if (this._batchSelectMode) {
            const batchCount = this._batchSelectedIds?.size || 0;
            html += `<div id="batch-toolbar" class="batch-toolbar">
                <span class="batch-toolbar-count" id="batch-toolbar-count">${batchCount} selected</span>
                <button class="batch-toolbar-btn" onclick="App._batchSelectAll()">Select All</button>
                <button class="batch-toolbar-btn" onclick="App._batchClearSelection()">Clear</button>
                <button class="batch-toolbar-btn batch-toolbar-primary" id="batch-edit-btn" onclick="App.openBatchEditModal()" ${!batchCount ? 'disabled' : ''}>Edit Selected</button>
                <button class="batch-toolbar-btn batch-toolbar-exit" onclick="App._batchToggleSelect()">&#x2715; Exit</button>
            </div>`;
        }

        target.innerHTML = html;
    },

    changeVideosSort(sort) {
        this.videosSort = sort;
        this.videosPage = 1;
        this.loadVideosPage();
    },

    filterVideosType(type) {
        this.videosMediaType = type;
        this._videosView = 'all';
        this.videosPage = 1;
        this.loadVideosPage();
    },

    filterVideosGenre(genre) {
        this.videosGenre = genre;
        this.videosCustomCategory = null; // genre and custom category are mutually exclusive
        this.videosCustomGenreId = null;
        this.videosPage = 1;
        this.loadVideosPage();
    },

    filterVideosCustomCategory(cat) {
        this.videosCustomCategory = cat;
        this.videosGenre = null; // clear genre when selecting a folder category
        this.videosCustomGenreId = null;
        this.videosPage = 1;
        this.loadVideosPage();
    },

    filterVideosCustomGenre(id) {
        this.videosCustomGenreId = id;
        this.videosGenre = null;
        this.videosCustomCategory = null;
        this.videosPage = 1;
        this.loadVideosPage();
    },

    _cgVideoNav(delta) {
        this._cgVideoPage = Math.max(0, (this._cgVideoPage || 0) + delta);
        this.loadVideosPage();
    },

    _cgMusicNav(delta) {
        this._cgMusicPage = Math.max(0, (this._cgMusicPage || 0) + delta);
        const el = document.getElementById('main-content');
        if (el) this.renderGenres(el);
    },

    // ─── Collections ────────────────────────────────────────
    async showCollectionsView() {
        this._currentVideoSection = 'movies';
        this._videosView = 'collections';
        this.videosMediaType = null;
        this.videosGenre = null;
        this.videosCustomCategory = null;
        this.videosCustomGenreId = null;
        const el = document.getElementById('main-content');
        el.innerHTML = '<div style="padding:40px;text-align:center;color:var(--text-secondary)">Loading collections…</div>';

        const collections = await this.api('collections');
        document.getElementById('page-title').innerHTML = `<span>${this.t('page.movies')}</span>`;

        // Re-render the page header — Movies page context (Collections is part of Movies)
        let html = `<div class="page-header"><h1>${this.t('page.movies')}</h1></div>`;
        html += '<div style="display:flex;gap:8px;margin-bottom:20px;flex-wrap:wrap">';
        html += `<button class="filter-chip" onclick="App.filterVideosType(null)">${this.t('nav.movies')}</button>`;
        html += `<button class="filter-chip active" onclick="App.showCollectionsView()"><svg style="width:13px;height:13px;stroke:currentColor;fill:none;stroke-width:2;vertical-align:middle;margin-right:4px"><use href="#icon-film"/></svg>Collections</button>`;
        html += '</div>';

        if (!collections || collections.length === 0) {
            html += this.emptyState('No Collections Found', 'Collections are populated automatically from TMDB metadata. Fetch metadata for your movies to see them here.');
            el.innerHTML = html;
            return;
        }

        html += `<div class="mv-stats-bar"><span>${collections.length} collection${collections.length !== 1 ? 's' : ''}</span><span>${collections.reduce((s, c) => s + c.ownedCount, 0)} owned</span><span>${collections.reduce((s, c) => s + c.watchedCount, 0)} watched</span></div>`;
        html += '<div class="collections-grid">';
        collections.forEach(c => {
            const posterSrc = c.posterPath ? `/videometa/${c.posterPath}` : '';
            const pct = c.ownedCount > 0 ? Math.round((c.watchedCount / c.ownedCount) * 100) : 0;
            const allWatched = c.totalCount > 0 && c.ownedCount >= c.totalCount;
            html += `<div class="collection-card" onclick="App.openCollectionDetail(${c.id})">
                <div class="collection-card-thumb">
                    ${posterSrc
                        ? `<img src="${posterSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="collection-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`
                        : `<span class="collection-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`}
                    <span class="collection-count-badge">${c.ownedCount} owned</span>
                    ${allWatched ? `<span class="mv-watched-badge" style="top:8px;left:8px;right:auto"><svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>COMPLETE</span>` : ''}
                </div>
                <div class="collection-card-info">
                    <div class="collection-card-title">${this.esc(c.name)}</div>
                    <div class="collection-card-count">${c.ownedCount} in library</div>
                    <div class="collection-progress-wrap">
                        <div class="collection-progress-bar" style="width:${pct}%"></div>
                    </div>
                    <div class="collection-progress-label">${c.watchedCount} of ${c.ownedCount} watched</div>
                </div>
            </div>`;
        });
        html += '</div>';
        el.innerHTML = html;
    },

    async openCollectionDetail(id) {
        const el = document.getElementById('main-content');
        el.innerHTML = '<div style="padding:40px;text-align:center;color:var(--text-secondary)">Loading…</div>';

        const data = await this.api(`collections/${id}`);
        if (!data) { el.innerHTML = this.emptyState('Error', 'Could not load collection.'); return; }

        const posterSrc = data.posterPath ? `/videometa/${data.posterPath}` : '';
        const totalCount = data.totalCount || data.movieCount || 0;
        const ownedCount = data.ownedCount || 0;
        const watchedCount = data.watchedCount || 0;
        const pct = totalCount > 0 ? Math.round((watchedCount / totalCount) * 100) : 0;
        document.getElementById('page-title').innerHTML = `<span>${this.esc(data.name)}</span>`;

        let html = `<div class="collection-detail-header">`;
        if (posterSrc) {
            html += `<div class="collection-detail-poster"><img src="${posterSrc}" alt="${this.esc(data.name)}"></div>`;
        }
        html += `<div class="collection-detail-meta">
            <h1>${this.esc(data.name)}</h1>
            <div class="collection-detail-stat">${totalCount} movie${totalCount !== 1 ? 's' : ''} &bull; ${ownedCount} in library</div>
            <div class="collection-progress-wrap"><div class="collection-progress-bar" style="width:${pct}%"></div></div>
            <div class="collection-progress-label" style="margin-bottom:16px">${watchedCount} of ${totalCount} watched (${pct}%)</div>
            <button class="btn-primary" onclick="App.showCollectionsView()">&#8592; All Collections</button>
        </div></div>`;

        html += '<div class="collection-movies-grid">';
        const buildHdrBadge = hdr => {
            if (!hdr) return '';
            const cls = hdr === 'Dolby Vision' ? 'mv-hdr-dv' : hdr === 'HDR10+' ? 'mv-hdr-plus' : hdr === 'HLG' ? 'mv-hdr-hlg' : '';
            return `<span class="mv-hdr-badge ${cls}">${this.esc(hdr)}</span>`;
        };
        data.movies.forEach(v => {
            const owned = v.owned !== false;
            const thumbSrc = v.posterPath ? `/videometa/${v.posterPath}` : '';
            const tmdbThumb = !thumbSrc && v.tmdbPosterUrl ? v.tmdbPosterUrl : '';
            const imgSrc = thumbSrc || tmdbThumb;
            const resLabel = v.height >= 2160 ? '4K' : v.height >= 1080 ? '1080p' : v.height >= 720 ? '720p' : v.height > 0 ? v.height + 'p' : '';
            const watchedBadge = v.isWatched ? `<span class="mv-watched-badge"><svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>WATCHED</span>` : '';
            const ratingBadge = v.rating > 0 ? `<span class="mv-rating-badge" style="background:${v.rating >= 7 ? 'rgba(39,174,96,.85)' : v.rating >= 5 ? 'rgba(241,196,15,.85)' : 'rgba(231,76,60,.85)'}">${v.rating.toFixed(1)}</span>` : '';
            const cardClick = owned ? `onclick="App.openVideoDetail(${v.id})"` : '';
            const playBtn = owned ? `<button class="mv-card-play" onclick="event.stopPropagation(); App.playVideo(${v.id})">&#9654;</button>` : '';
            const notOwnedOverlay = !owned ? `<div class="collection-not-owned-overlay"><span>Not in Library</span></div>` : '';
            html += `<div class="mv-card mv-card-poster${!owned ? ' collection-not-owned' : ''}" ${cardClick}>
                <div class="mv-card-thumb mv-poster-thumb">
                    ${imgSrc
                        ? `<img src="${imgSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                        : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                    ${notOwnedOverlay}
                    ${watchedBadge}
                    ${resLabel ? `<span class="mv-format-badge mv-format-ok">${resLabel}</span>` : ''}
                    ${buildHdrBadge(v.hdrFormat)}
                    ${playBtn}
                    ${ratingBadge}
                </div>
                <div class="mv-card-info">
                    <div class="mv-card-title">${this.esc(v.title)}</div>
                    <div class="mv-card-artist">${v.year || ''}</div>
                </div>
            </div>`;
        });
        html += '</div>';
        el.innerHTML = html;
    },

    goVideosPage(page) {
        const totalPages = Math.ceil(this.videosTotal / this.videosPerPage);
        if (page < 1 || page > totalPages) return;
        this.videosPage = page;
        this.loadVideosPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    renderVideosPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goVideosPage(${currentPage - 1})">&laquo; ${this.t('pagination.prev')}</button>`;
        const pages = [];
        pages.push(1);
        let rangeStart = Math.max(2, currentPage - 2);
        let rangeEnd = Math.min(totalPages - 1, currentPage + 2);
        if (rangeStart > 2) pages.push('...');
        for (let p = rangeStart; p <= rangeEnd; p++) pages.push(p);
        if (rangeEnd < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goVideosPage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goVideosPage(${currentPage + 1})">${this.t('pagination.next')} &raquo;</button>`;
        html += '</div>';
        return html;
    },

    // ── Anime ────────────────────────────────────────────────────────

    async renderAnime(el) {
        this.animePage = 1;
        this.animeSort = 'recent';
        this.animeGenre = null;
        this.animeSearch = '';
        await this.loadAnimePage(el);
    },

    async loadAnimePage(el) {
        const target = el || document.getElementById('main-content');
        let url = `videos?mediaType=anime&limit=${this.videosPerPage}&page=${this.animePage}&sort=${this.animeSort}&grouped=true`;
        if (this.animeGenre) url += `&genre=${encodeURIComponent(this.animeGenre)}`;
        if (this.animeSearch) url += `&search=${encodeURIComponent(this.animeSearch)}`;

        const data = await this.api(url);
        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load anime library.'); return; }

        this.animeTotal = data.total;
        const totalPages = Math.ceil(data.total / this.videosPerPage);

        let html = `<div class="page-header"><h1>${this.t('nav.anime')}</h1>
            <div class="filter-bar">`;
        const sortLabels = { recent: this.t('sort.recent'), title: this.t('sort.title'), year: this.t('sort.year'), series: this.t('sort.series') };
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.animeSort === key ? ' active' : ''}" onclick="App.changeAnimeSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

        if (!data.videos || data.videos.length === 0) {
            html += this.emptyState(this.t('nav.anime'), this.t('anime.noAnime'));
            target.innerHTML = html;
            return;
        }

        if (totalPages > 1) {
            html += `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;color:var(--text-secondary);font-size:13px">
                <span>${data.total} episodes</span>
                <span>Page ${this.animePage} of ${totalPages}</span>
            </div>`;
        }

        html += '<div class="mv-grid">';
        const buildHdrBadge = hdr => {
            if (!hdr) return '';
            const cls = hdr === 'Dolby Vision' ? 'mv-hdr-dv' : hdr === 'HDR10+' ? 'mv-hdr-plus' : hdr === 'HLG' ? 'mv-hdr-hlg' : '';
            return `<span class="mv-hdr-badge ${cls}">${this.esc(hdr)}</span>`;
        };

        data.videos.forEach(v => {
            const thumbSrc = v.posterPath ? `/videometa/${v.posterPath}` : (v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '');
            const hasPoster = !!v.posterPath;
            const dur = this.formatDuration(v.duration);
            const resLabel = v.height >= 2160 ? '4K' : v.height >= 1080 ? '1080p' : v.height >= 720 ? '720p' : v.height > 0 ? v.height + 'p' : '';
            const hdrBadge = buildHdrBadge(v.hdrFormat || '');
            const ratingBadge = v.rating > 0 ? `<span class="mv-rating-badge" style="background:${v.rating >= 7 ? 'rgba(39,174,96,.85)' : 'rgba(241,196,15,.85)'}">${v.rating.toFixed(1)}</span>` : '';

            if (v.type === 'series') {
                const epLabel = `${v.episodeCount} Ep${v.episodeCount !== 1 ? 's' : ''}`;
                html += `<div class="mv-card mv-card-series${hasPoster ? ' mv-card-poster' : ''}" onclick="App.openSeriesDetail('${this.esc(v.seriesName).replace(/'/g, "\\'")}', 'anime')" data-series="${this.esc(v.seriesName)}">
                    <div class="mv-card-thumb${hasPoster ? ' mv-poster-thumb' : ''}">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        ${resLabel ? `<span class="mv-format-badge mv-format-ok">${resLabel}</span>` : ''}
                        ${hdrBadge}
                        <span class="video-type-badge video-type-anime">Anime</span>
                        <span class="mv-episode-badge">${v.episodeCount}</span>
                        ${ratingBadge}
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.seriesName)}</div>
                        <div class="mv-card-artist">${epLabel}</div>
                        <div class="mv-card-meta"><span>${v.year || ''}</span><span>${this.formatSize(v.sizeBytes)}</span></div>
                    </div>
                    <button class="mv-card-menu-btn" onclick="event.stopPropagation(); App.showVideoMenu(${v.firstEpisodeId || v.id}, 'anime', event)" title="More options">&#8942;</button>
                </div>`;
            } else {
                const subtitle = v.seriesName ? `${v.seriesName}${v.episode ? ' EP' + String(v.episode).padStart(2,'0') : ''}` : (v.year || '');
                html += `<div class="mv-card${hasPoster ? ' mv-card-poster' : ''}" onclick="App.openVideoDetail(${v.id})">
                    <div class="mv-card-thumb${hasPoster ? ' mv-poster-thumb' : ''}">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        ${resLabel ? `<span class="mv-format-badge mv-format-ok">${resLabel}</span>` : ''}
                        ${hdrBadge}
                        <span class="video-type-badge video-type-anime">Anime</span>
                        ${ratingBadge}
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${this.esc(subtitle)}</div>
                        <div class="mv-card-meta"><span>${this.formatSize(v.sizeBytes)}</span></div>
                    </div>
                    <button class="mv-card-menu-btn" onclick="event.stopPropagation(); App.showVideoMenu(${v.id}, 'anime', event)" title="More options">&#8942;</button>
                </div>`;
            }
        });
        html += '</div>';

        if (totalPages > 1) html += this.renderAnimePagination(this.animePage, totalPages);
        target.innerHTML = html;
    },

    changeAnimeSort(sort) { this.animeSort = sort; this.animePage = 1; this.loadAnimePage(); },
    filterAnimeGenre(genre) { this.animeGenre = genre; this.animePage = 1; this.loadAnimePage(); },
    searchAnime(val) {
        clearTimeout(this._animeSearchTimer);
        this._animeSearchTimer = setTimeout(() => { this.animeSearch = val; this.animePage = 1; this.loadAnimePage(); }, 350);
    },
    goAnimePage(page) {
        const totalPages = Math.ceil(this.animeTotal / this.videosPerPage);
        if (page < 1 || page > totalPages) return;
        this.animePage = page;
        this.loadAnimePage();
        document.getElementById('main-content').scrollTop = 0;
    },
    renderAnimePagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goAnimePage(${currentPage - 1})">&laquo; ${this.t('pagination.prev')}</button>`;
        const pages = [1];
        let start = Math.max(2, currentPage - 2), end = Math.min(totalPages - 1, currentPage + 2);
        if (start > 2) pages.push('...');
        for (let p = start; p <= end; p++) pages.push(p);
        if (end < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goAnimePage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goAnimePage(${currentPage + 1})">${this.t('pagination.next')} &raquo;</button>`;
        html += '</div>';
        return html;
    },

    async startAnimeScan() {
        const result = await this.apiPost('scan/anime');
        if (result) this.pollAnimeScanStatus();
    },

    async pollAnimeScanStatus() {
        const poll = async () => {
            const s = await this.api('scan/anime/status');
            const statusEl = document.getElementById('anime-scan-status');
            if (!s) return;
            if (statusEl) {
                if (s.isScanning) {
                    statusEl.innerHTML = `<div class="scan-bar">
                        <div class="scan-text">${this.t('status.scanning')} ${s.processedFiles}/${s.totalFiles} files (${s.percentComplete}%)</div>
                        <div class="scan-progress"><div class="scan-progress-fill" style="width:${s.percentComplete}%"></div></div>
                    </div>`;
                } else {
                    statusEl.innerHTML = `<div class="scan-bar"><div class="scan-text">${s.message || 'Scan complete'}</div></div>`;
                    this.loadBadgeCounts();
                }
            }
            if (s.isScanning) setTimeout(poll, 1000);
        };
        poll();
    },

    _langCodeToName(code) {
        const map = {
            eng:'English',en:'English',fre:'French',fra:'French',fr:'French',
            ger:'German',deu:'German',de:'German',spa:'Spanish',es:'Spanish',
            ita:'Italian',it:'Italian',por:'Portuguese',pt:'Portuguese',
            rus:'Russian',ru:'Russian',jpn:'Japanese',ja:'Japanese',
            kor:'Korean',ko:'Korean',zho:'Chinese',zh:'Chinese',
            chi:'Chinese',ara:'Arabic',ar:'Arabic',hin:'Hindi',hi:'Hindi',
            pol:'Polish',pl:'Polish',nld:'Dutch',nl:'Dutch',dut:'Dutch',
            swe:'Swedish',sv:'Swedish',nor:'Norwegian',no:'Norwegian',
            dan:'Danish',da:'Danish',fin:'Finnish',fi:'Finnish',
            tur:'Turkish',tr:'Turkish',ces:'Czech',cs:'Czech',cze:'Czech',
            hun:'Hungarian',hu:'Hungarian',ron:'Romanian',ro:'Romanian',rum:'Romanian',
            ell:'Greek',el:'Greek',gre:'Greek',heb:'Hebrew',he:'Hebrew',
            tha:'Thai',th:'Thai',vie:'Vietnamese',vi:'Vietnamese',
            ind:'Indonesian',id:'Indonesian',ukr:'Ukrainian',uk:'Ukrainian',
            cat:'Catalan',ca:'Catalan',hrv:'Croatian',hr:'Croatian',
            slk:'Slovak',sk:'Slovak',slo:'Slovak',bul:'Bulgarian',bg:'Bulgarian',
            lit:'Lithuanian',lt:'Lithuanian',lav:'Latvian',lv:'Latvian',
            est:'Estonian',et:'Estonian',und:'Undetermined'
        };
        const c = (code || '').toLowerCase().trim();
        return map[c] || code.toUpperCase();
    },

    async _selectAudioTrack(video) {
        const langs = (video.audioLanguages || '').split(',').map(l => l.trim()).filter(Boolean);
        if (langs.length <= 1) return 0;
        return new Promise(resolve => {
            const overlay = document.createElement('div');
            overlay.className = 'audio-select-overlay';
            let btns = '';
            langs.forEach((lang, i) => {
                const name = this._langCodeToName(lang);
                btns += `<button class="audio-select-btn" data-index="${i}">${this.esc(name)}</button>`;
            });
            overlay.innerHTML = `<div class="audio-select-dialog">
                <div class="audio-select-title">${this.t('player.selectAudio') || 'Select Audio Language'}</div>
                <div class="audio-select-buttons">${btns}</div>
            </div>`;
            overlay.addEventListener('click', e => {
                const btn = e.target.closest('.audio-select-btn');
                if (btn) {
                    overlay.remove();
                    resolve(parseInt(btn.dataset.index, 10));
                } else if (e.target === overlay) {
                    overlay.remove();
                    resolve(-1);
                }
            });
            document.body.appendChild(overlay);
        });
    },

    async openVideoDetail(id) {
        // Await the transcode stop BEFORE stopAllMedia so the old FFmpeg process
        // has fully exited before stream-info starts a new one (fixes Next Episode bug)
        await this.stopCurrentTranscode();
        this.stopAllMedia();
        const video = await this.api(`videos/${id}`);
        if (!video) return;

        // If multiple audio languages, ask user to select
        const audioTrack = await this._selectAudioTrack(video);
        if (audioTrack < 0) return; // user cancelled

        // Get smart stream info to determine playback method
        const trackParam = audioTrack > 0 ? `?audioTrack=${audioTrack}` : '';
        const [streamInfo, ratingSum] = await Promise.all([
            this.api(`stream-info/${id}${trackParam}`),
            this.api(`ratings/summary/video/${id}`)
        ]);

        const el = document.getElementById('main-content');
        if (!this._isMobile()) document.getElementById('page-title').innerHTML = `<span>${this.esc(video.title)}</span>`;

        const resLabel = video.height >= 2160 ? '4K' : video.height >= 1080 ? '1080p' : video.height >= 720 ? '720p' : video.height > 0 ? video.height + 'p' : '';
        const hdrLabel = video.hdrFormat || '';
        // Don't show year for TV/anime episodes — year in filename is unreliable (often encode year)
        const showYear = video.year && !video.seriesName;
        const episodeInfo = video.mediaType === 'tv' && video.seriesName
            ? `${this.esc(video.seriesName)} &middot; S${String(video.season||'?').padStart(2,'0')}E${String(video.episode||'?').padStart(2,'0')}`
            : '';
        const backPage = (video.mediaType === 'movie') ? 'movies' : 'tvshows';
        const backLabel = (video.mediaType === 'movie') ? this.t('btn.backToMovies') : this.t('btn.backToTvShows');
        const backBtn = `<button class="filter-chip" onclick="App.navigate('${backPage}')">&laquo; ${backLabel}</button>`;
        const isHLS = streamInfo && streamInfo.type === 'hls';

        // ── Helper: build content-rating badge ──
        const buildContentRating = cr => {
            if (!cr) return '';
            const r = cr.toUpperCase();
            const cls = (r === 'R' || r === 'NC-17' || r === 'TV-MA') ? 'mv-cr-red'
                      : (r === 'PG-13' || r === 'TV-14') ? 'mv-cr-yellow'
                      : (r === 'G' || r === 'TV-G' || r === 'TV-Y') ? 'mv-cr-green'
                      : '';
            return `<span class="mv-content-rating ${cls}">${this.esc(cr)}</span>`;
        };

        // ── Helper: build ratings row ──
        const buildRatingsRow = () => {
            let chips = '';
            if (video.rating > 0) {
                const score = video.rating.toFixed(1);
                chips += `<span class="mv-rating-chip mv-rating-tmdb">
                    <svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 14H9V8h2v8zm4 0h-2V8h2v8z"/></svg>
                    ${score}/10 <span style="font-size:10px;font-weight:400;margin-left:2px;opacity:.7">TMDB</span></span>`;
            }
            if (video.imdbRating) {
                chips += `<span class="mv-rating-chip mv-rating-imdb">
                    <svg viewBox="0 0 24 24" fill="currentColor"><rect x="2" y="4" width="20" height="16" rx="3"/><path fill="var(--bg-primary)" d="M5 8h2v8H5zm5 0h2l1.5 4L15 8h2v8h-2v-4l-1 3h-2l-1-3v4h-2z"/></svg>
                    ${this.esc(video.imdbRating)}/10 <span style="font-size:10px;font-weight:400;margin-left:2px;opacity:.7">IMDb</span></span>`;
            }
            if (video.rottenTomatoesRating) {
                const pct = parseInt(video.rottenTomatoesRating) || 0;
                const fresh = pct >= 60;
                chips += `<span class="mv-rating-chip ${fresh ? 'mv-rating-rt-fresh' : 'mv-rating-rt-rotten'}">
                    <svg viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="12" r="10"/><path fill="var(--bg-primary)" d="M12 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 10c-2.2 0-4-1.8-4-4h2c0 1.1.9 2 2 2s2-.9 2-2h2c0 2.2-1.8 4-4 4z"/></svg>
                    ${this.esc(video.rottenTomatoesRating)} <span style="font-size:10px;font-weight:400;margin-left:2px;opacity:.7">RT</span></span>`;
            }
            if (video.metacriticRating) {
                chips += `<span class="mv-rating-chip mv-rating-metacritic">
                    <svg viewBox="0 0 24 24" fill="currentColor"><rect x="2" y="2" width="20" height="20" rx="4"/><text x="5" y="17" fill="var(--bg-primary)" font-size="11" font-weight="900">MC</text></svg>
                    ${this.esc(video.metacriticRating)} <span style="font-size:10px;font-weight:400;margin-left:2px;opacity:.7">MC</span></span>`;
            }
            return chips ? `<div class="mv-ratings-row">${chips}</div>` : '';
        };

        // ── Helper: build genre chips ──
        const buildGenreChips = g => !g ? '' :
            `<div class="mv-genre-chips">${g.split(',').map(s => `<span class="mv-genre-chip">${this.esc(s.trim())}</span>`).join('')}</div>`;

        // ── Helper: build combined director + writer strip (one horizontal row) ──
        const buildCrewStrip = () => {
            const makeCard = (name, photo, roleLabel) => {
                const src = photo ? `/videometa/${this.esc(photo)}` : '';
                const img = src ? `<img src="${src}" loading="lazy" alt="" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">` : '';
                const fallback = `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;opacity:.2;${src ? 'display:none' : ''}"><svg style="width:36px;height:36px;fill:none;stroke:currentColor;stroke-width:1.5"><use href="#icon-film"/></svg></div>`;
                return `<div class="director-card"><div class="director-photo">${img}${fallback}</div><div class="director-name">${this.esc(name)}</div><div class="director-role">${roleLabel}</div></div>`;
            };
            let cards = '';
            if (video.directorJson) {
                try { JSON.parse(video.directorJson).forEach(d => { cards += makeCard(d.name, d.photo, this.t('label.director')); }); } catch(e) {}
            }
            if (video.writerJson) {
                try { JSON.parse(video.writerJson).forEach(w => { cards += makeCard(w.name, w.photo, this.t('label.writer')); }); } catch(e) {}
            }
            if (cards) return `<div class="director-strip">${cards}</div>`;
            // Fallback: plain text when no JSON yet
            return video.director
                ? `<div class="director-row"><span class="director-label">${this.t('label.director')}:</span> ${this.esc(video.director)}</div>`
                : '';
        };

        // ── Helper: build studios row ──
        const buildStudiosRow = () => {
            if (!video.studiosJson) return '';
            try {
                const studios = JSON.parse(video.studiosJson);
                if (!studios.length) return '';
                const items = studios.map(s => {
                    const logoSrc = s.logo ? `/videometa/${this.esc(s.logo)}` : '';
                    const logoEl = logoSrc
                        ? `<img src="${logoSrc}" loading="lazy" alt="${this.esc(s.name)}" onerror="this.style.display='none'">`
                        : `<span style="font-size:9px;color:var(--text-muted);text-align:center;line-height:1.2;padding:2px">${this.esc(s.name)}</span>`;
                    return `<div class="studio-card">
                        <div class="studio-logo-box">${logoEl}</div>
                        <span class="studio-name-label">${this.esc(s.name)}</span>
                    </div>`;
                }).join('');
                return `<div class="studios-strip">${items}</div>`;
            } catch(e) { return ''; }
        };

        // ── Helper: build cast strip ──
        const buildCastStrip = castJson => {
            if (!castJson) return '';
            try {
                const cast = JSON.parse(castJson);
                if (!cast.length) return '';
                const items = cast.map(c => {
                    const photo = c.photo ? `/videometa/${c.photo}` : '';
                    const click = c.actorId ? `onclick="App.openActorDetail(${c.actorId})"` : '';
                    const img = photo
                        ? `<img src="${photo}" loading="lazy" alt="" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">`
                        : '';
                    const fallback = `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;opacity:.2;${photo?'display:none':''}"><svg style="width:36px;height:36px;fill:none;stroke:currentColor;stroke-width:1.5"><use href="#icon-users"/></svg></div>`;
                    return `<div class="mv-cast-member" ${click} style="${c.actorId ? 'cursor:pointer' : ''}">
                        <div class="mv-cast-photo">${img}${fallback}</div>
                        <div class="mv-cast-name">${this.esc(c.name)}</div>
                        <div class="mv-cast-char">${this.esc(c.character || '')}</div>
                    </div>`;
                }).join('');
                return `<div class="settings-section">
                    <h3 style="margin-bottom:4px">${this.t('section.cast')}</h3>
                    <div class="mv-cast-strip">${items}</div>
                </div>`;
            } catch(e) { return ''; }
        };

        // ── Actions bar + popup (injected into each header variant on the right) ──
        const videoActionsAndPopup = `<div style="flex-shrink:0;position:relative">
            <div class="vp-actions-bar" style="justify-content:flex-end">
                ${isHLS && streamInfo && streamInfo.transcodeId ? `<button onclick="App.toggleTranscodeOverlay()" id="btn-stats-toggle" class="mv-stats-btn"><svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-activity"/></svg> Live Stats</button>` : ''}
                <button id="btn-video-details" class="mv-stats-btn" onclick="App.toggleVideoDetailsPopup()"><svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-list"/></svg> Video Details</button>
                <button class="vp-fav-btn${video.isFavourite ? ' active' : ''}" id="vp-fav-btn" onclick="App.toggleVideoFav(${id}, this)" title="Add to Favourites">&#10084;</button>
                <div class="rating-widget vp-inline-rating" data-mt="video" data-mid="${id}">${this._buildRatingWidgetInner(ratingSum || {}, 'video', id)}</div>
                <button onclick="App.toggleEQPanel()" id="btn-eq-page" class="mv-stats-btn"><svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-equalizer"/></svg> ${this.t('player.equalizer', 'Equalizer')}</button>
            </div>
            <div id="video-details-popup" class="vp-details-popup" style="display:none">
                <div class="setting-row setting-row-col"><span class="setting-label">Filename</span><span class="setting-value" style="word-break:break-all;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;line-height:1.4">${this.esc(video.fileName)}</span></div>
                <div class="setting-row"><span class="setting-label">Format</span><span class="setting-value">${this.esc(video.format)}</span></div>
                <div class="setting-row"><span class="setting-label">Resolution</span><span class="setting-value">${video.resolution || 'Unknown'} (${video.width}x${video.height})</span></div>
                <div class="setting-row"><span class="setting-label">Video Codec</span><span class="setting-value">${this.esc(video.codec || 'Unknown')}</span></div>
                ${video.hdrFormat ? `<div class="setting-row"><span class="setting-label">HDR Format</span><span class="setting-value">${this.esc(video.hdrFormat)}</span></div>` : ''}
                ${video.videoBitrate ? `<div class="setting-row"><span class="setting-label">Video Bitrate</span><span class="setting-value">${video.videoBitrate} kbps</span></div>` : ''}
                <div class="setting-row"><span class="setting-label">Audio Codec</span><span class="setting-value">${this.esc(video.audioCodec || 'Unknown')}</span></div>
                <div class="setting-row"><span class="setting-label">Audio Channels</span><span class="setting-value">${video.audioChannels}</span></div>
                ${video.audioLanguages ? `<div class="setting-row"><span class="setting-label">Audio Languages</span><span class="setting-value">${this.esc(video.audioLanguages)}</span></div>` : ''}
                ${video.subtitleLanguages ? `<div class="setting-row"><span class="setting-label">Subtitles</span><span class="setting-value">${this.esc(video.subtitleLanguages)}</span></div>` : ''}
                <div class="setting-row"><span class="setting-label">Browser Compatible</span><span class="setting-value">${video.mp4Compliant ? 'Yes' : 'No'}</span></div>
                ${streamInfo && streamInfo.mode ? `<div class="setting-row"><span class="setting-label">Stream Mode</span><span class="setting-value">${this.esc(streamInfo.mode)}${streamInfo.reason ? ' (' + this.esc(streamInfo.reason) + ')' : ''}</span></div>` : ''}
            </div>
        </div>`;

        let html = '';

        if (video.backdropPath) {
            // ── Full Plex-style hero: backdrop fills top, poster + meta overlaid at bottom ──
            html += `<div class="video-hero">
                <div class="video-hero-backdrop" style="background-image:url('/videometa/${video.backdropPath}')">
                    <div class="video-hero-scrim"></div>
                    <div class="video-hero-top">${backBtn}</div>
                    <div class="video-hero-body">
                        ${video.posterPath ? `<img class="video-hero-poster" src="/videometa/${video.posterPath}" alt="">` : ''}
                        <div class="video-hero-meta">
                            ${episodeInfo ? `<div class="video-hero-episode">${episodeInfo}</div>` : ''}
                            <h1 class="video-hero-title">${this.esc(video.title)}</h1>
                            <div class="video-hero-sub">${showYear ? video.year + ' &middot; ' : ''}${this.formatDuration(video.duration)}${resLabel ? ' &middot; ' + resLabel : ''}${hdrLabel ? ' &middot; ' + hdrLabel : ''}</div>
                            <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:6px">
                                ${buildRatingsRow()}
                                ${buildContentRating(video.contentRating)}
                            </div>
                            ${buildGenreChips(video.genre)}
                            ${video.overview ? `<p class="video-hero-overview">${this.esc(video.overview)}</p>` : ''}
                            ${buildCrewStrip()}${buildStudiosRow()}
                        </div>
                    </div>
                </div>
            </div>`;
        } else if (video.posterPath) {
            // ── Poster + info (no backdrop available) ──
            html += `<div style="display:flex;gap:22px;margin-bottom:20px;align-items:flex-start">
                <img src="/videometa/${video.posterPath}" style="width:160px;min-width:160px;border-radius:10px;box-shadow:0 6px 24px rgba(0,0,0,.5)" alt="">
                <div style="flex:1;min-width:0">
                    <div style="margin-bottom:8px">${backBtn}</div>
                    <h1 style="margin:0 0 4px;font-size:1.6rem;line-height:1.2">${this.esc(video.title)}</h1>
                    <div style="color:var(--text-muted);font-size:13px;margin-bottom:8px">
                        ${episodeInfo ? episodeInfo + ' &middot; ' : ''}${showYear ? video.year + ' &middot; ' : ''}${this.formatDuration(video.duration)}${resLabel ? ' &middot; ' + resLabel : ''}${hdrLabel ? ' &middot; ' + hdrLabel : ''}
                    </div>
                    <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:4px">
                        ${buildRatingsRow()}
                        ${buildContentRating(video.contentRating)}
                    </div>
                    ${buildGenreChips(video.genre)}
                    ${buildCrewStrip()}${buildStudiosRow()}
                    ${video.overview ? `<p style="color:var(--text-secondary);font-size:13px;line-height:1.6;margin-top:10px;max-height:130px;overflow-y:auto;padding-right:4px">${this.esc(video.overview)}</p>` : ''}
                </div>
            </div>`;
        } else {
            // ── No images at all ──
            html += `<div class="page-header">
                <div>
                    <div style="margin-bottom:8px">${backBtn}</div>
                    <h1>${this.esc(video.title)}</h1>
                    <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">
                        ${episodeInfo ? episodeInfo + ' &middot; ' : ''}${showYear ? video.year + ' &middot; ' : ''}${this.formatDuration(video.duration)} &middot; ${this.formatSize(video.sizeBytes)}${resLabel ? ' &middot; ' + resLabel : ''}${hdrLabel ? ' &middot; ' + hdrLabel : ''}
                    </div>
                    <div style="margin-top:10px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                        ${buildRatingsRow()}
                        ${buildContentRating(video.contentRating)}
                    </div>
                    ${buildGenreChips(video.genre)}
                    ${video.overview ? `<p style="color:var(--text-secondary);font-size:14px;line-height:1.6;margin-top:10px">${this.esc(video.overview)}</p>` : ''}
                    ${buildCrewStrip()}${buildStudiosRow()}
                </div>
            </div>`;
        }

        // ── Cast strip (BEFORE the video player, like Plex) ──
        html += buildCastStrip(video.castJson);

        // ── Subtitle toolbar + actions bar (same row, above player) ──
        html += `<div style="display:flex;justify-content:space-between;align-items:flex-start;gap:8px">
        <div class="subtitle-toolbar" id="subtitle-toolbar-${video.id}">
            <button class="subtitle-fetch-btn" onclick="App.toggleSubtitlePanel(${video.id}, ${!!video.subtitleLanguages})">
                <svg style="width:14px;height:14px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;vertical-align:-2px;margin-right:5px"><use href="#icon-file-text"/></svg>${this.t('subtitle.fetchSubtitles')}
            </button>
            <span id="subtitle-active-label-${video.id}" class="subtitle-active-label"></span>
            <button id="subtitle-off-${video.id}" class="subtitle-off-btn" onclick="App.disableSubtitles(${video.id})" title="Disable subtitles" style="display:none"><svg width="14" height="14" viewBox="0 0 14 14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="7" cy="7" r="6"/><line x1="4.5" y1="4.5" x2="9.5" y2="9.5"/><line x1="9.5" y1="4.5" x2="4.5" y2="9.5"/></svg></button>
            <div id="subtitle-panel-${video.id}" class="subtitle-panel" style="display:none">
                <div id="subtitle-embedded-${video.id}" style="display:none">
                    <div class="subtitle-section-label">${this.t('subtitle.builtIn')}</div>
                    <div id="subtitle-embedded-list-${video.id}" class="subtitle-results"></div>
                </div>
                <div class="subtitle-section-label">${this.t('subtitle.online')}</div>
                <div class="subtitle-panel-inner">
                    <select id="subtitle-lang-${video.id}" class="subtitle-lang-select">
                        <option value="en">English</option>
                        <option value="fr">Français</option>
                        <option value="de">Deutsch</option>
                        <option value="es">Español</option>
                        <option value="it">Italiano</option>
                        <option value="pt">Português</option>
                        <option value="ru">Русский</option>
                        <option value="pl">Polski</option>
                        <option value="nl">Nederlands</option>
                        <option value="sv">Svenska</option>
                        <option value="ro">Română</option>
                        <option value="uk">Українська</option>
                        <option value="ar">العربية</option>
                        <option value="zh-hans">中文</option>
                        <option value="ja">日本語</option>
                        <option value="ko">한국어</option>
                    </select>
                    <button class="subtitle-search-btn" onclick="App.searchSubtitles(${video.id}, '${this.esc(video.imdbId || '')}', '${this.esc(video.title)}', ${video.year || 'null'})">${this.t('subtitle.search')}</button>
                </div>
                <div id="subtitle-results-${video.id}" class="subtitle-results"></div>
            </div>
        </div>
        ${videoActionsAndPopup}
        </div>`;

        // ── Video player ──
        html += `<div class="mv-player-container">
            <video id="video-player" controls autoplay class="mv-player">
                ${!isHLS ? `<source src="/api/stream-video/${video.id}${audioTrack > 0 ? '?audioTrack=' + audioTrack : ''}" type="video/mp4">` : ''}
                Your browser does not support the video tag.
            </video>
            ${isHLS && streamInfo && streamInfo.transcodeId ? `<div id="transcode-stats-overlay" class="tc-overlay" style="display:none"><div id="tc-stats-text">Starting...</div></div>` : ''}
            ${(video.mediaType === 'tv' || video.mediaType === 'anime') ? `<div id="video-up-next" class="video-up-next"></div>` : ''}
            ${video.mediaType === 'tv' && video.season && video.episode ? `<div id="ep-play-overlay" style="display:none;position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);pointer-events:none;z-index:10;text-align:center;transition:opacity .6s"><span style="font-size:44px;font-weight:700;color:#39ff14;text-shadow:0 0 12px rgba(0,0,0,1),0 2px 8px rgba(0,0,0,.9),0 0 24px rgba(57,255,20,.4);letter-spacing:.04em">${this.t('misc.season')} ${video.season}, ${this.t('misc.episode')} ${video.episode}</span></div>` : ''}
            <button id="skip-intro-btn" class="skip-intro-btn" style="display:none" onclick="App.skipIntro()">${this.t('btn.skipIntro')}</button>`;
        if (isHLS && streamInfo.transcodeId) {
            const modeLabel = streamInfo.mode === 'transcode-cached' ? this.t('misc.cached') : this.t('misc.transcoding');
            html += `<div id="transcode-info" style="display:flex;align-items:center;gap:10px;padding:8px 12px;background:rgba(255,255,255,.04);border-radius:0 0 8px 8px;font-size:12px;color:var(--text-secondary)">
                <span>${modeLabel}${streamInfo.reason ? ' — ' + this.esc(streamInfo.reason) : ''}</span>
                ${streamInfo.mode === 'transcode' ? `<button onclick="App.stopCurrentTranscode()" style="margin-left:auto;background:none;border:1px solid rgba(255,255,255,.15);color:var(--text-secondary);padding:3px 10px;border-radius:12px;cursor:pointer;font-size:11px">${this.t('btn.stopTranscode')}</button>` : ''}
            </div>`;
            this._currentTranscodeId = streamInfo.transcodeId;
        }
        html += `</div>`;

        // ── Episode navigation (TV series + Anime) ──
        if (video.mediaType === 'tv' || video.mediaType === 'anime') {
            html += `<div id="ep-nav-bar" class="ep-nav-bar"></div>`;
        }

        el.innerHTML = html;

        // Auto-hide transcode info bar after 15 seconds
        if (isHLS && streamInfo.transcodeId) {
            clearTimeout(this._transcodeInfoTimeout);
            this._transcodeInfoTimeout = setTimeout(() => {
                const bar = document.getElementById('transcode-info');
                if (bar) {
                    bar.style.transition = 'opacity .6s';
                    bar.style.opacity = '0';
                    setTimeout(() => { if (bar) bar.style.display = 'none'; }, 600);
                }
            }, 15000);
        }

        // Connect EQ to the video player
        const videoEl = document.getElementById('video-player');
        if (videoEl) this.connectEQToElement(videoEl);

        // Episode overlay: show "Season X, Episode Y" for 6s on play (TV only)
        if (video.mediaType === 'tv' && video.season && video.episode && videoEl) {
            const epOverlay = document.getElementById('ep-play-overlay');
            if (epOverlay) {
                const showEpOverlay = () => {
                    epOverlay.style.opacity = '1';
                    epOverlay.style.display = 'block';
                    clearTimeout(this._epOverlayTimeout);
                    this._epOverlayTimeout = setTimeout(() => {
                        epOverlay.style.opacity = '0';
                        setTimeout(() => { epOverlay.style.display = 'none'; }, 600);
                    }, 6000);
                };
                videoEl.addEventListener('play', showEpOverlay, { once: true });
            }
        }

        // If HLS stream, use playVideoStream to load via HLS.js (or native HLS for Safari).
        if (isHLS) {
            const hlsUrl = streamInfo.masterPlaylistUrl || streamInfo.playlistUrl;
            if (videoEl && hlsUrl) {
                // For HEVC passthrough: if the browser can't decode HEVC, HLS.js will
                // fire fatal media errors. Fall back to a forced SDR transcode automatically.
                const hevcFallback = streamInfo.mode === 'hevc-passthrough' ? async () => {
                    console.warn('HEVC passthrough failed (codec unsupported), retrying with SDR transcode');
                    // Stop the passthrough immediately — it's useless if the browser can't decode HEVC
                    if (this._currentTranscodeId) this.apiPost(`stop-transcode/${this._currentTranscodeId}`, {}).catch(() => {});
                    const sep = trackParam ? '&' : '?';
                    const fbInfo = await this.api(`stream-info/${id}${trackParam}${sep}forceTranscode=true`).catch(e => { console.error('Transcode fallback request failed:', e); return null; });
                    const el = document.getElementById('video-player');
                    if (fbInfo && fbInfo.playlistUrl && el) {
                        // Update tracking to the real SDR transcode — fixes "Transcode complete" showing
                        // too early and ensures stopCurrentTranscode() kills the right FFmpeg process
                        if (fbInfo.transcodeId) {
                            this._currentTranscodeId = fbInfo.transcodeId;
                            const infoBar = document.getElementById('transcode-info');
                            if (infoBar) {
                                const span = infoBar.querySelector('span');
                                if (span) span.textContent = this.t('misc.transcoding') + (fbInfo.reason ? ' — ' + fbInfo.reason : '');
                                infoBar.style.opacity = '1';
                                infoBar.style.display = 'flex';
                                clearTimeout(this._transcodeInfoTimeout);
                                this._transcodeInfoTimeout = setTimeout(() => {
                                    infoBar.style.transition = 'opacity .6s';
                                    infoBar.style.opacity = '0';
                                    setTimeout(() => { infoBar.style.display = 'none'; }, 600);
                                }, 15000);
                            }
                        }
                        this.playVideoStream(el, fbInfo.playlistUrl);
                    }
                } : null;
                this.playVideoStream(videoEl, hlsUrl, hevcFallback);
            }
        }

        // Set up progress tracking (Continue Watching)
        this._setupVideoProgressTracking(video.id);

        // Trakt.tv scrobbling
        if (this._initCfg?.traktConnected && this._initCfg?.traktScrobbleEnabled) {
            this._setupTraktScrobble(video);
        }

        // Intro Skipper — fetch chapter-based intro timestamps and set up Skip Intro button
        if (this._initCfg?.introSkipperEnabled) {
            this._setupIntroSkipper(video.id);
        }

        // If TV or Anime episode, set up "Up Next" auto-play when finished
        if ((video.mediaType === 'tv' || video.mediaType === 'anime') && video.seriesName) {
            this._setupNextEpisode(video);
        }
    },

    async _setupNextEpisode(video) {
        // Fetch prev and next in parallel
        const [next, prev] = await Promise.all([
            this.api(`videos/${video.id}/next`),
            this.api(`videos/${video.id}/previous`)
        ]);

        // Populate nav bar
        this._renderEpisodeNavBar(prev, next);

        // Attach ended listener only if there is a next episode
        if (next) {
            const videoEl = document.getElementById('video-player');
            if (videoEl) videoEl.addEventListener('ended', () => this._showUpNext(next), { once: true });
        }
    },

    _setupTraktScrobble(video) {
        this._stopTraktScrobble();
        const videoEl = document.getElementById('video-player');
        if (!videoEl) return;

        const scrobble = (action, progressOverride) => {
            const dur = videoEl.duration;
            const pos = videoEl.currentTime;
            const pct = progressOverride ?? (dur > 0 && isFinite(dur) ? (pos / dur) * 100 : 0);
            this.apiPost('trakt/scrobble', { videoId: video.id, action, progress: Math.round(pct * 10) / 10 })
                .catch(() => {});
        };

        // Fire "start" immediately (progress 0)
        scrobble('start', 0);

        this._traktOnPlay   = () => scrobble('start');
        this._traktOnPause  = () => { const pct = videoEl.duration > 0 ? (videoEl.currentTime / videoEl.duration) * 100 : 0; scrobble(pct >= 80 ? 'stop' : 'pause'); };
        this._traktOnEnded  = () => scrobble('stop', 100);

        videoEl.addEventListener('play',  this._traktOnPlay);
        videoEl.addEventListener('pause', this._traktOnPause);
        videoEl.addEventListener('ended', this._traktOnEnded);
    },

    _stopTraktScrobble() {
        const videoEl = document.getElementById('video-player');
        if (videoEl) {
            if (this._traktOnPlay)  videoEl.removeEventListener('play',  this._traktOnPlay);
            if (this._traktOnPause) videoEl.removeEventListener('pause', this._traktOnPause);
            if (this._traktOnEnded) videoEl.removeEventListener('ended', this._traktOnEnded);
        }
        // Send a final "stop" if video was playing when we tear down
        if (this._traktOnPlay) {
            const el = document.getElementById('video-player');
            if (el && !el.paused && !el.ended) {
                const pct = el.duration > 0 && isFinite(el.duration) ? (el.currentTime / el.duration) * 100 : 0;
                // Fire-and-forget — we're navigating away
                if (this._progressVideoId)
                    this.apiPost('trakt/scrobble', { videoId: this._progressVideoId, action: 'stop', progress: Math.round(pct * 10) / 10 }).catch(() => {});
            }
        }
        this._traktOnPlay = null;
        this._traktOnPause = null;
        this._traktOnEnded = null;
    },

    async _setupIntroSkipper(videoId) {
        // Remove any leftover timeupdate listener + button from a previous video
        if (this._introTimeupdateHandler) {
            const el = document.getElementById('video-player');
            if (el) el.removeEventListener('timeupdate', this._introTimeupdateHandler);
            this._introTimeupdateHandler = null;
        }
        const btn = document.getElementById('skip-intro-btn');
        if (btn) btn.style.display = 'none';

        const data = await this.api(`videos/${videoId}/intro`);
        if (!data || !data.hasIntro) return;

        const { start, end } = data;
        const videoEl = document.getElementById('video-player');
        if (!videoEl) return;

        const showBtn = () => {
            const btn = document.getElementById('skip-intro-btn');
            if (btn) { btn.style.display = 'flex'; btn.style.opacity = '1'; }
        };
        const hideBtn = () => {
            const btn = document.getElementById('skip-intro-btn');
            if (btn) { btn.style.opacity = '0'; setTimeout(() => { if (btn) btn.style.display = 'none'; }, 300); }
        };

        this._introEnd = end;
        this._introTimeupdateHandler = () => {
            const t = videoEl.currentTime;
            if (t >= start && t < end) showBtn();
            else hideBtn();
        };
        videoEl.addEventListener('timeupdate', this._introTimeupdateHandler);
    },

    skipIntro() {
        const videoEl = document.getElementById('video-player');
        if (videoEl && this._introEnd) videoEl.currentTime = this._introEnd;
        const btn = document.getElementById('skip-intro-btn');
        if (btn) { btn.style.opacity = '0'; setTimeout(() => { btn.style.display = 'none'; }, 300); }
    },

    _renderEpisodeNavBar(prev, next) {
        const bar = document.getElementById('ep-nav-bar');
        if (!bar || (!prev && !next)) { if (bar) bar.style.display = 'none'; return; }

        const buildBtn = (ep, dir) => {
            if (!ep) return `<div class="ep-nav-btn ep-nav-${dir} ep-nav-disabled"></div>`;
            const epLabel = ep.season
                ? `S${String(ep.season).padStart(2,'0')}E${String(ep.episode||'?').padStart(2,'0')}`
                : `EP${String(ep.episode||'?').padStart(2,'0')}`;
            const thumb = ep.thumbnailPath
                ? `<img src="/videometa/${ep.thumbnailPath}" class="ep-nav-thumb" alt="" onerror="this.style.display='none'">`
                : `<div class="ep-nav-thumb ep-nav-thumb-placeholder"></div>`;
            const arrow = dir === 'prev'
                ? `<svg style="width:18px;height:18px;stroke:currentColor;fill:none;stroke-width:2;flex-shrink:0"><use href="#icon-skip-back"/></svg>`
                : `<svg style="width:18px;height:18px;stroke:currentColor;fill:none;stroke-width:2;flex-shrink:0"><use href="#icon-skip-forward"/></svg>`;
            const label = dir === 'prev' ? 'Previous Episode' : 'Next Episode';
            return `<button class="ep-nav-btn ep-nav-${dir}" onclick="App.openVideoDetail(${ep.id})" title="${label}">
                ${arrow}
                ${thumb}
                <div class="ep-nav-info">
                    <div class="ep-nav-label">${label}</div>
                    <div class="ep-nav-ep">${epLabel}</div>
                    <div class="ep-nav-title">${this.esc(ep.title)}</div>
                </div>
            </button>`;
        };

        bar.innerHTML = buildBtn(prev, 'prev') + buildBtn(next, 'next');
    },

    _showUpNext(next) {
        const panel = document.getElementById('video-up-next');
        if (!panel) return;

        const epLabel = next.season
            ? `S${String(next.season).padStart(2,'0')}E${String(next.episode||'?').padStart(2,'0')}`
            : `EP${String(next.episode||'?').padStart(2,'0')}`;
        const thumb = next.thumbnailPath
            ? `<img src="/videometa/${next.thumbnailPath}" class="video-up-next-thumb" alt="" onerror="this.style.display='none'">`
            : `<div class="video-up-next-thumb" style="display:flex;align-items:center;justify-content:center"><svg style="width:32px;height:32px;fill:none;stroke:rgba(255,255,255,.3);stroke-width:1.5"><use href="#icon-play"/></svg></div>`;

        panel.innerHTML = `
            <div class="video-up-next-label">Up Next</div>
            ${thumb}
            <div class="video-up-next-info">
                <div class="video-up-next-ep">${epLabel} · ${this.esc(next.seriesName)}</div>
                <div class="video-up-next-title">${this.esc(next.title)}</div>
            </div>
            <div class="video-up-next-bar-wrap"><div class="video-up-next-bar" id="upnext-bar"></div></div>
            <div class="video-up-next-countdown" id="upnext-countdown">Playing in 10s</div>
            <div class="video-up-next-btns">
                <button class="video-up-next-play" onclick="App._playUpNext(${next.id})">▶ Play Now</button>
                <button class="video-up-next-cancel" onclick="App._cancelUpNext()">Cancel</button>
            </div>`;
        panel.classList.add('visible');

        // 10-second countdown
        let secs = 10;
        const bar = document.getElementById('upnext-bar');
        const countdown = document.getElementById('upnext-countdown');
        // Kick off bar fill (CSS transition handles smooth animation per second)
        requestAnimationFrame(() => { if (bar) bar.style.width = '100%'; });

        this._upNextTimer = setInterval(() => {
            secs--;
            if (countdown) countdown.textContent = `Playing in ${secs}s`;
            if (secs <= 0) {
                clearInterval(this._upNextTimer);
                this._upNextTimer = null;
                this._playUpNext(next.id);
            }
        }, 1000);
    },

    _playUpNext(id) {
        this._cancelUpNext();
        this.openVideoDetail(id);
    },

    _cancelUpNext() {
        if (this._upNextTimer) { clearInterval(this._upNextTimer); this._upNextTimer = null; }
        const panel = document.getElementById('video-up-next');
        if (panel) panel.classList.remove('visible');
    },

    // ─── Subtitle Methods ──────────────────────────────────────────────────────

    toggleSubtitlePanel(videoId, hasEmbedded) {
        const panel = document.getElementById(`subtitle-panel-${videoId}`);
        if (!panel) return;
        const isOpen = panel.style.display !== 'none';
        panel.style.display = isOpen ? 'none' : 'block';
        if (!isOpen && hasEmbedded) {
            this._loadEmbeddedSubtitles(videoId);
        }
    },

    async _loadEmbeddedSubtitles(videoId) {
        const listEl = document.getElementById(`subtitle-embedded-list-${videoId}`);
        const sectionEl = document.getElementById(`subtitle-embedded-${videoId}`);
        if (!listEl || !sectionEl) return;
        if (listEl.dataset.loaded) return;
        listEl.innerHTML = `<div class="subtitle-loading">${this.t('subtitle.searching')}</div>`;
        sectionEl.style.display = 'block';
        const data = await this.api(`videos/${videoId}/embedded-subtitles`);
        if (!data || !data.tracks || data.tracks.length === 0) {
            sectionEl.style.display = 'none';
            return;
        }
        listEl.dataset.loaded = '1';
        const langName = lang => ({ eng:'English', ita:'Italian', fra:'French', deu:'German', spa:'Spanish', por:'Portuguese', rus:'Russian', jpn:'Japanese', kor:'Korean', zho:'Chinese', ara:'Arabic', pol:'Polish', nld:'Dutch', swe:'Swedish', und:'Unknown' })[lang] || lang.toUpperCase();
        let html = '';
        for (const t of data.tracks) {
            const forcedTag = t.forced ? `<span class="subtitle-hi-tag">FORCED</span>` : '';
            const hiTag = t.hi ? `<span class="subtitle-hi-tag">SDH</span>` : '';
            const label = t.title || langName(t.lang);
            html += `<div class="subtitle-result-row" onclick="App.applyEmbeddedSubtitle(${videoId}, ${t.trackIndex}, '${this.esc(label)}', this)">
                <span class="subtitle-prov-tag">MKV</span>${forcedTag}${hiTag}
                <span class="subtitle-filename">${this.esc(label)}</span>
            </div>`;
        }
        listEl.innerHTML = html;
    },

    async applyEmbeddedSubtitle(videoId, trackIndex, label, rowEl) {
        document.querySelectorAll('.subtitle-result-row').forEach(r => r.classList.remove('selected'));
        if (rowEl) rowEl.classList.add('selected', 'loading');
        const prevText = rowEl ? rowEl.innerHTML : '';
        if (rowEl) rowEl.innerHTML = `<span>${this.t('subtitle.downloading')}</span>`;

        const vttUrl = `/api/subtitles/embedded/${videoId}/${trackIndex}`;
        const videoEl = document.getElementById('video-player');
        if (videoEl) {
            videoEl.querySelectorAll('track[data-nexusm]').forEach(t => t.remove());
            const track = document.createElement('track');
            track.kind = 'subtitles';
            track.srclang = 'und';
            track.label = label;
            track.setAttribute('data-nexusm', '1');
            videoEl.appendChild(track);
            const tt = videoEl.textTracks[videoEl.textTracks.length - 1];
            if (tt) tt.mode = 'showing';
            track.src = vttUrl + '?t=' + Date.now();
        }
        const labelEl = document.getElementById(`subtitle-active-label-${videoId}`);
        if (labelEl) labelEl.textContent = label;
        const offBtn = document.getElementById(`subtitle-off-${videoId}`);
        if (offBtn) offBtn.style.display = '';
        const panel = document.getElementById(`subtitle-panel-${videoId}`);
        if (panel) panel.style.display = 'none';
        if (rowEl) { rowEl.innerHTML = prevText; rowEl.classList.remove('loading'); }
    },

    async searchSubtitles(videoId, imdbId, title, year) {
        const langEl = document.getElementById(`subtitle-lang-${videoId}`);
        const resultsEl = document.getElementById(`subtitle-results-${videoId}`);
        if (!langEl || !resultsEl) return;

        const language = langEl.value;
        resultsEl.innerHTML = `<div class="subtitle-loading">${this.t('subtitle.searching')}</div>`;

        const data = await this.api(`videos/${videoId}/subtitles?language=${encodeURIComponent(language)}`);
        if (!data || !data.results || data.results.length === 0) {
            resultsEl.innerHTML = `<div class="subtitle-no-results">${this.t('subtitle.noResults')}</div>`;
            return;
        }

        let html = '';
        for (const sub of data.results) {
            const hiTag = sub.hearingImpaired ? `<span class="subtitle-hi-tag">${this.t('subtitle.hearingImpaired')}</span>` : '';
            const provTag = `<span class="subtitle-prov-tag">${sub.provider === 'opensubtitles' ? 'OS' : 'SDL'}</span>`;
            const dlCount = sub.downloads > 0 ? `<span class="subtitle-dl-count">${sub.downloads.toLocaleString()} ${this.t('subtitle.downloads')}</span>` : '';
            html += `<div class="subtitle-result-row" onclick="App.applySubtitle(${videoId}, '${this.esc(sub.provider)}', '${this.esc(sub.fileId)}', '${language}', this)">
                ${provTag}${hiTag}
                <span class="subtitle-filename">${this.esc(sub.fileName || sub.fileId)}</span>
                ${dlCount}
            </div>`;
        }
        resultsEl.innerHTML = html;
    },

    async applySubtitle(videoId, provider, fileId, language, rowEl) {
        // Visual feedback on the clicked row
        document.querySelectorAll('.subtitle-result-row').forEach(r => r.classList.remove('selected'));
        if (rowEl) rowEl.classList.add('selected', 'loading');

        const resultsEl = document.getElementById(`subtitle-results-${videoId}`);
        const prevText = rowEl ? rowEl.innerHTML : '';
        if (rowEl) rowEl.innerHTML = `<span>${this.t('subtitle.downloading')}</span>`;

        const data = await this.apiPost(`videos/${videoId}/subtitles/download`, { provider, fileId, language });

        if (!data || !data.vttUrl) {
            if (rowEl) { rowEl.innerHTML = prevText; rowEl.classList.remove('loading'); }
            const errEl = document.getElementById(`subtitle-results-${videoId}`);
            if (errEl) errEl.insertAdjacentHTML('afterbegin', `<div class="subtitle-error">${this.t('subtitle.error')}</div>`);
            return;
        }

        // Inject or replace <track> in the video player
        const videoEl = document.getElementById('video-player');
        if (videoEl) {
            // Remove existing managed tracks
            videoEl.querySelectorAll('track[data-nexusm]').forEach(t => t.remove());

            const track = document.createElement('track');
            track.kind = 'subtitles';
            track.srclang = language;
            track.label = language.toUpperCase();
            track.setAttribute('data-nexusm', '1');
            videoEl.appendChild(track);

            // Set mode immediately (before src so track is in textTracks list)
            const tt = videoEl.textTracks[videoEl.textTracks.length - 1];
            if (tt) tt.mode = 'showing';

            // Now set src — browser loads the VTT; mode is already 'showing'
            track.src = data.vttUrl + '?t=' + Date.now();
        }

        // Update the active label and close panel
        const labelEl = document.getElementById(`subtitle-active-label-${videoId}`);
        if (labelEl) labelEl.textContent = language.toUpperCase();
        const offBtn = document.getElementById(`subtitle-off-${videoId}`);
        if (offBtn) offBtn.style.display = '';
        const panel = document.getElementById(`subtitle-panel-${videoId}`);
        if (panel) panel.style.display = 'none';
        if (rowEl) { rowEl.innerHTML = prevText; rowEl.classList.remove('loading'); }
    },

    disableSubtitles(videoId) {
        const videoEl = document.getElementById('video-player');
        if (videoEl) {
            videoEl.querySelectorAll('track[data-nexusm]').forEach(t => t.remove());
            for (const track of videoEl.textTracks) track.mode = 'disabled';
        }
        const labelEl = document.getElementById(`subtitle-active-label-${videoId}`);
        if (labelEl) labelEl.textContent = '';
        const offBtn = document.getElementById(`subtitle-off-${videoId}`);
        if (offBtn) offBtn.style.display = 'none';
    },

    async _setupVideoProgressTracking(videoId) {
        this._stopVideoProgressTracking();
        this._progressVideoId = videoId;

        const player = document.getElementById('video-player');
        if (!player) return;

        // Fetch saved progress and seek if > 30s
        const progress = await this.api(`videos/${videoId}/progress`).catch(() => null);

        // Guard: if navigation happened while awaiting, abort this stale setup
        if (this._progressVideoId !== videoId) return;

        if (progress && progress.position > 30 && !progress.completed) {
            const seek = () => { player.currentTime = progress.position; };
            if (player.readyState >= 1) seek();
            else player.addEventListener('loadedmetadata', seek, { once: true });
        }

        const saveProgress = (pos, dur) => {
            // Guard against Infinity duration (HLS streams during initial load)
            if (!(dur > 0) || !isFinite(dur) || !(pos > 0)) return;
            fetch(`/api/videos/${videoId}/progress`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ position: pos, duration: dur })
            }).catch(() => {});
        };

        // Save progress every 10 seconds
        this._videoProgressInterval = setInterval(() => {
            saveProgress(player.currentTime, player.duration);
        }, 10000);

        // Save on pause
        this._onVideoProgressPause = () => {
            saveProgress(player.currentTime, player.duration);
        };

        // Save on ended (marks complete at 100%)
        this._onVideoProgressEnded = () => {
            const dur = player.duration;
            if (isFinite(dur) && dur > 0) {
                saveProgress(dur, dur);
            }
            this._stopVideoProgressTracking();
        };

        player.addEventListener('pause', this._onVideoProgressPause);
        player.addEventListener('ended', this._onVideoProgressEnded);
    },

    _stopVideoProgressTracking() {
        clearInterval(this._videoProgressInterval);
        this._videoProgressInterval = null;
        const player = document.getElementById('video-player');
        if (player) {
            // Final save before stopping so navigation never loses the current position
            if (this._progressVideoId) {
                const pos = player.currentTime;
                const dur = player.duration;
                if (pos > 0 && isFinite(dur) && dur > 0) {
                    fetch(`/api/videos/${this._progressVideoId}/progress`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ position: pos, duration: dur })
                    }).catch(() => {});
                }
            }
            if (this._onVideoProgressPause) player.removeEventListener('pause', this._onVideoProgressPause);
            if (this._onVideoProgressEnded) player.removeEventListener('ended', this._onVideoProgressEnded);
        }
        this._onVideoProgressPause = null;
        this._onVideoProgressEnded = null;
        this._progressVideoId = null;
        // Also clean up Trakt scrobble listeners
        this._stopTraktScrobble();
    },

    async openSeriesDetail(seriesName, mediaType = null) {
        this.stopAllMedia();
        const mtParam = mediaType ? `&mediaType=${mediaType}` : '';
        const data = await this.api(`videos?series=${encodeURIComponent(seriesName)}${mtParam}&sort=series&limit=500`);
        if (!data || !data.videos || data.videos.length === 0) return;

        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(seriesName)}</span>`;

        const episodes = data.videos;
        const seasons = {};
        let totalDuration = 0;
        let seriesMeta = { posterPath: '', backdropPath: '', overview: '', genre: '', rating: 0, contentRating: '', castJson: '' };
        episodes.forEach(ep => {
            const s = ep.season || 0;
            if (!seasons[s]) seasons[s] = [];
            seasons[s].push(ep);
            totalDuration += ep.duration || 0;
            // Collect series-level metadata from first episode that has it
            if (!seriesMeta.posterPath && ep.posterPath) seriesMeta.posterPath = ep.posterPath;
            if (!seriesMeta.backdropPath && ep.backdropPath) seriesMeta.backdropPath = ep.backdropPath;
            if (!seriesMeta.overview && ep.overview) seriesMeta.overview = ep.overview;
            if (!seriesMeta.genre && ep.genre) seriesMeta.genre = ep.genre;
            if (!seriesMeta.rating && ep.rating) seriesMeta.rating = ep.rating;
            if (!seriesMeta.contentRating && ep.contentRating) seriesMeta.contentRating = ep.contentRating;
            if (!seriesMeta.castJson && ep.castJson) seriesMeta.castJson = ep.castJson;
            if (!seriesMeta.directorJson && ep.directorJson) seriesMeta.directorJson = ep.directorJson;
            if (!seriesMeta.studiosJson && ep.studiosJson) seriesMeta.studiosJson = ep.studiosJson;
        });
        const seasonNums = Object.keys(seasons).map(Number).sort((a, b) => a - b);

        // ── Helper: build content rating badge ──
        const buildSeriesContentRating = cr => {
            if (!cr) return '';
            const r = cr.toUpperCase();
            const cls = (r === 'R' || r === 'NC-17' || r === 'TV-MA') ? 'mv-cr-red'
                      : (r === 'PG-13' || r === 'TV-14') ? 'mv-cr-yellow'
                      : (r === 'G' || r === 'TV-G' || r === 'TV-Y') ? 'mv-cr-green'
                      : '';
            return `<span class="mv-content-rating ${cls}">${this.esc(cr)}</span>`;
        };

        // ── Helper: build TMDB rating chip ──
        const buildSeriesRatingsRow = () => {
            if (!seriesMeta.rating || seriesMeta.rating <= 0) return '';
            const score = seriesMeta.rating.toFixed(1);
            return `<div class="mv-ratings-row"><span class="mv-rating-chip mv-rating-tmdb">
                <svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 14H9V8h2v8zm4 0h-2V8h2v8z"/></svg>
                ${score}/10 <span style="font-size:10px;font-weight:400;margin-left:2px;opacity:.7">TMDB</span></span></div>`;
        };

        // ── Helper: build genre chips ──
        const buildSeriesGenreChips = g => !g ? '' :
            `<div class="mv-genre-chips">${g.split(',').map(s => `<span class="mv-genre-chip">${this.esc(s.trim())}</span>`).join('')}</div>`;

        // ── Helper: build combined director strip (creators for TV) ──
        const buildSeriesCrewStrip = () => {
            if (!seriesMeta.directorJson) return '';
            try {
                const dirs = JSON.parse(seriesMeta.directorJson);
                if (!dirs.length) return '';
                const items = dirs.map(d => {
                    const photo = d.photo ? `/videometa/${this.esc(d.photo)}` : '';
                    const img = photo ? `<img src="${photo}" loading="lazy" alt="" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">` : '';
                    const fallback = `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;opacity:.2;${photo ? 'display:none' : ''}"><svg style="width:36px;height:36px;fill:none;stroke:currentColor;stroke-width:1.5"><use href="#icon-film"/></svg></div>`;
                    return `<div class="director-card"><div class="director-photo">${img}${fallback}</div><div class="director-name">${this.esc(d.name)}</div><div class="director-role">${this.t('label.director')}</div></div>`;
                }).join('');
                return `<div class="director-strip">${items}</div>`;
            } catch(e) { return ''; }
        };

        // ── Helper: build studios row ──
        const buildSeriesStudiosRow = () => {
            if (!seriesMeta.studiosJson) return '';
            try {
                const studios = JSON.parse(seriesMeta.studiosJson);
                if (!studios.length) return '';
                const items = studios.map(s => {
                    const logoSrc = s.logo ? `/videometa/${this.esc(s.logo)}` : '';
                    const logoEl = logoSrc
                        ? `<img src="${logoSrc}" loading="lazy" alt="${this.esc(s.name)}" onerror="this.style.display='none'">`
                        : `<span style="font-size:9px;color:var(--text-muted);text-align:center;line-height:1.2;padding:2px">${this.esc(s.name)}</span>`;
                    return `<div class="studio-card">
                        <div class="studio-logo-box">${logoEl}</div>
                        <span class="studio-name-label">${this.esc(s.name)}</span>
                    </div>`;
                }).join('');
                return `<div class="studios-strip">${items}</div>`;
            } catch(e) { return ''; }
        };

        // ── Helper: build cast strip ──
        const buildSeriesCastStrip = castJson => {
            if (!castJson) return '';
            try {
                const cast = JSON.parse(castJson);
                if (!cast.length) return '';
                const items = cast.map(c => {
                    const photo = c.photo ? `/videometa/${c.photo}` : '';
                    const click = c.actorId ? `onclick="App.openActorDetail(${c.actorId})"` : '';
                    const img = photo
                        ? `<img src="${photo}" loading="lazy" alt="" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">`
                        : '';
                    const fallback = `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;opacity:.2;${photo?'display:none':''}"><svg style="width:36px;height:36px;fill:none;stroke:currentColor;stroke-width:1.5"><use href="#icon-users"/></svg></div>`;
                    return `<div class="mv-cast-member" ${click} style="${c.actorId ? 'cursor:pointer' : ''}">
                        <div class="mv-cast-photo">${img}${fallback}</div>
                        <div class="mv-cast-name">${this.esc(c.name)}</div>
                        <div class="mv-cast-char">${this.esc(c.character || '')}</div>
                    </div>`;
                }).join('');
                return `<div class="settings-section">
                    <h3 style="margin-bottom:4px">${this.t('section.cast')}</h3>
                    <div class="mv-cast-strip">${items}</div>
                </div>`;
            } catch(e) { return ''; }
        };

        const seriesBackBtn = `<button class="filter-chip" onclick="App.navigate('tvshows')">&laquo; ${this.t('btn.backToTvShows')}</button>`;
        const seriesSubInfo = `${seasonNums.length} Season${seasonNums.length !== 1 ? 's' : ''} &middot; ${episodes.length} Episode${episodes.length !== 1 ? 's' : ''} &middot; ${this.formatDuration(totalDuration)}`;

        let html = '';

        if (seriesMeta.backdropPath) {
            // ── Full Plex-style hero ──
            html += `<div class="video-hero">
                <div class="video-hero-backdrop" style="background-image:url('/videometa/${seriesMeta.backdropPath}')">
                    <div class="video-hero-scrim"></div>
                    <div class="video-hero-top">${seriesBackBtn}</div>
                    <div class="video-hero-body">
                        ${seriesMeta.posterPath ? `<img class="video-hero-poster" src="/videometa/${seriesMeta.posterPath}" alt="">` : ''}
                        <div class="video-hero-meta">
                            <h1 class="video-hero-title">${this.esc(seriesName)}</h1>
                            <div class="video-hero-sub">${seriesSubInfo}</div>
                            <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:6px">
                                ${buildSeriesRatingsRow()}
                                ${buildSeriesContentRating(seriesMeta.contentRating)}
                            </div>
                            ${buildSeriesGenreChips(seriesMeta.genre)}
                            ${seriesMeta.overview ? `<p class="video-hero-overview">${this.esc(seriesMeta.overview)}</p>` : ''}
                            ${buildSeriesCrewStrip()}${buildSeriesStudiosRow()}
                        </div>
                    </div>
                </div>
            </div>`;
        } else if (seriesMeta.posterPath) {
            // ── Poster + info (no backdrop) ──
            html += `<div style="display:flex;gap:20px;margin-bottom:16px;align-items:flex-start">
                <img src="/videometa/${seriesMeta.posterPath}" style="width:140px;min-width:140px;border-radius:10px;box-shadow:0 6px 24px rgba(0,0,0,.5)" alt="">
                <div style="flex:1;min-width:0">
                    <div style="margin-bottom:8px">${seriesBackBtn}</div>
                    <h1 style="margin:0 0 4px">${this.esc(seriesName)}</h1>
                    <div style="color:var(--text-muted);font-size:13px;margin-bottom:8px">${seriesSubInfo}</div>
                    <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:6px">
                        ${buildSeriesRatingsRow()}
                        ${buildSeriesContentRating(seriesMeta.contentRating)}
                    </div>
                    ${buildSeriesGenreChips(seriesMeta.genre)}
                    ${buildSeriesCrewStrip()}${buildSeriesStudiosRow()}
                    ${seriesMeta.overview ? `<p style="color:var(--text-secondary);font-size:13px;line-height:1.6;margin-top:10px;max-height:130px;overflow-y:auto;padding-right:4px">${this.esc(seriesMeta.overview)}</p>` : ''}
                </div>
            </div>`;
        } else {
            // ── No images ──
            html += `<div class="page-header">
                <div>
                    <div style="margin-bottom:8px">${seriesBackBtn}</div>
                    <h1>${this.esc(seriesName)}</h1>
                    <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">${seriesSubInfo}</div>
                    <div style="margin-top:10px;display:flex;align-items:center;gap:8px;flex-wrap:wrap">
                        ${buildSeriesRatingsRow()}
                        ${buildSeriesContentRating(seriesMeta.contentRating)}
                    </div>
                    ${buildSeriesGenreChips(seriesMeta.genre)}
                    ${seriesMeta.overview ? `<p style="color:var(--text-secondary);font-size:14px;line-height:1.6;margin-top:10px">${this.esc(seriesMeta.overview)}</p>` : ''}
                    ${buildSeriesCrewStrip()}${buildSeriesStudiosRow()}
                </div>
            </div>`;
        }

        // Cast section
        html += buildSeriesCastStrip(seriesMeta.castJson);

        // Render each season
        seasonNums.forEach(sNum => {
            const eps = seasons[sNum];
            const seasonLabel = sNum > 0 ? `Season ${sNum}` : 'Specials';
            html += `<div class="settings-section" style="margin-bottom:16px">
                <h3 style="margin:0 0 12px 0;font-size:16px;color:var(--text-primary)">${seasonLabel} <span style="color:var(--text-secondary);font-size:13px;font-weight:normal">(${eps.length} episode${eps.length !== 1 ? 's' : ''})</span></h3>`;
            eps.forEach(ep => {
                const epThumb = ep.thumbnailPath ? `/videothumb/${ep.thumbnailPath}` : '';
                const epNum = `S${(ep.season||0).toString().padStart(2,'0')}E${(ep.episode||0).toString().padStart(2,'0')}`;
                const epDur = this.formatDuration(ep.duration);
                const resLabel = ep.height >= 2160 ? '4K' : ep.height >= 1080 ? '1080p' : ep.height >= 720 ? '720p' : '';
                const epHdr = ep.hdrFormat || '';
                html += `<div class="mv-episode-row" onclick="App.openVideoDetail(${ep.id})" style="display:flex;align-items:center;gap:12px;padding:8px;border-radius:8px;cursor:pointer;transition:background .15s" onmouseenter="this.style.background='rgba(255,255,255,.05)'" onmouseleave="this.style.background='none'">
                    <div style="width:120px;min-width:120px;height:68px;border-radius:6px;overflow:hidden;background:rgba(255,255,255,.04);position:relative">
                        ${epThumb
                            ? `<img src="${epThumb}" loading="lazy" style="width:100%;height:100%;object-fit:cover" onerror="this.style.display='none'" alt="">`
                            : `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center"><svg style="width:28px;height:28px;stroke:currentColor;fill:none;stroke-width:1.5;opacity:.3"><use href="#icon-film"/></svg></div>`}
                    </div>
                    <div style="flex:1;min-width:0">
                        <div style="font-size:14px;font-weight:500;color:var(--text-primary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">
                            <span style="color:var(--accent);font-weight:600;margin-right:8px">${epNum}</span>${this.esc(ep.title)}
                        </div>
                        <div style="font-size:12px;color:var(--text-secondary);margin-top:2px">
                            ${epDur}${resLabel ? ' &middot; ' + resLabel : ''}${epHdr ? ' &middot; ' + epHdr : ''} &middot; ${ep.format || ''} &middot; ${this.formatSize(ep.sizeBytes)}
                        </div>
                    </div>
                    <button class="mv-card-play" onclick="event.stopPropagation(); App.openVideoDetail(${ep.id})" style="position:static;width:36px;height:36px;border-radius:50%;background:var(--accent);border:none;color:#fff;cursor:pointer;font-size:14px;display:flex;align-items:center;justify-content:center;opacity:.8;flex-shrink:0">&#9654;</button>
                    <button onclick="event.stopPropagation(); App.openVideoEditModal(${ep.id})" title="Edit metadata" style="width:30px;height:30px;border-radius:50%;background:rgba(255,255,255,.08);border:1px solid rgba(255,255,255,.12);color:var(--text-secondary);cursor:pointer;display:flex;align-items:center;justify-content:center;flex-shrink:0;opacity:.7;transition:opacity .15s" onmouseenter="this.style.opacity='1'" onmouseleave="this.style.opacity='.7'"><svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/></svg></button>
                </div>`;
            });
            html += '</div>';
        });

        el.innerHTML = html;
    },

    playVideo(id) {
        this.openVideoDetail(id);
    },

    toggleVideoDetails() {
        const arrow = document.getElementById('video-details-arrow');
        const body = document.getElementById('video-details-body');
        if (arrow) arrow.classList.toggle('collapsed');
        if (body) body.classList.toggle('collapsed');
    },

    toggleVideoDetailsPopup() {
        const popup = document.getElementById('video-details-popup');
        if (!popup) return;
        const isHidden = popup.style.display === 'none';
        popup.style.display = isHidden ? 'block' : 'none';
        if (isHidden) {
            const close = (e) => {
                const btn = document.getElementById('btn-video-details');
                if (!popup.contains(e.target) && e.target !== btn && !btn?.contains(e.target)) {
                    popup.style.display = 'none';
                    document.removeEventListener('click', close, true);
                }
            };
            setTimeout(() => document.addEventListener('click', close, true), 0);
        }
    },

    async stopCurrentTranscode() {
        if (!this._currentTranscodeId) return;
        // Clear immediately so concurrent/repeat calls are no-ops
        const transcodeId = this._currentTranscodeId;
        this._currentTranscodeId = null;
        this.stopTranscodeStatsPolling();
        try {
            await this.apiPost(`stop-transcode/${transcodeId}`, {});
            const info = document.getElementById('transcode-info');
            if (info) info.innerHTML = '<span style="color:var(--warning)">Transcode stopped</span>';
            const videoEl = document.getElementById('video-player');
            if (videoEl) this.stopVideoStream(videoEl);
        } catch (e) {
            console.error('Failed to stop transcode:', e);
        }
    },

    toggleTranscodeOverlay() {
        const overlay = document.getElementById('transcode-stats-overlay');
        if (!overlay) return;
        const visible = overlay.style.display !== 'none';
        const btn = document.getElementById('btn-stats-toggle');
        if (visible) {
            overlay.style.display = 'none';
            if (btn) btn.classList.remove('active');
            this.stopTranscodeStatsPolling();
        } else {
            overlay.style.display = 'block';
            if (btn) btn.classList.add('active');
            this.startTranscodeStatsPolling();
        }
    },

    startTranscodeStatsPolling() {
        this.stopTranscodeStatsPolling();
        this.fetchTranscodeStats();
        this._statsInterval = setInterval(() => this.fetchTranscodeStats(), 2000);
    },

    stopTranscodeStatsPolling() {
        if (this._statsInterval) {
            clearInterval(this._statsInterval);
            this._statsInterval = null;
        }
    },

    async fetchTranscodeStats() {
        if (!this._currentTranscodeId) return;
        try {
            const data = await this.api('transcode/status');
            if (!data || !data.active) return;
            const tc = data.active.find(t => t.transcodeId === this._currentTranscodeId);
            const el = document.getElementById('tc-stats-text');
            if (!el) return;
            if (!tc || tc.isCompleted) {
                el.textContent = 'Transcode complete';
                this.stopTranscodeStatsPolling();
            } else if (tc.lastStats) {
                el.textContent = tc.lastStats;
            } else {
                el.textContent = 'Starting...';
            }
        } catch(e) {}
    },

    // ─── Pictures Gallery ────────────────────────────────
    async renderPictures(el) {
        this.picturesPage = 1;
        this.picturesSort = 'recent';
        this.picturesCategory = null;
        await this.loadPicturesPage(el);
    },

    async loadPicturesPage(el) {
        const target = el || document.getElementById('main-content');
        let url = `pictures?limit=${this.picturesPerPage}&page=${this.picturesPage}&sort=${this.picturesSort}`;
        if (this.picturesCategory) url += `&category=${encodeURIComponent(this.picturesCategory)}`;

        const [data, categories] = await Promise.all([
            this.api(url),
            this.api('pictures/categories')
        ]);

        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load pictures.'); return; }
        this.picturesTotal = data.total;
        const totalPages = Math.ceil(data.total / this.picturesPerPage);

        let html = `<div class="page-header"><h1>${this.t('page.pictures')}</h1>
            <div class="filter-bar">`;
        const sortLabels = { recent: this.t('sort.recent'), name: this.t('sort.name'), date: this.t('sort.dateTaken'), size: this.t('sort.size') };
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.picturesSort === key ? ' active' : ''}" onclick="App.changePicturesSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

        // Category filter chips
        if (categories && categories.length > 0) {
            html += '<div style="display:flex;flex-wrap:wrap;gap:8px;margin-bottom:16px">';
            html += `<button class="filter-chip${!this.picturesCategory ? ' active' : ''}" onclick="App.filterPictureCategory(null)">${this.t('filter.allPictures')}</button>`;
            categories.forEach(c => {
                const isActive = this.picturesCategory === c.name;
                const displayName = c.name || 'Uncategorized';
                html += `<button class="filter-chip${isActive ? ' active' : ''}" onclick="App.filterPictureCategory('${this.esc(c.name)}')">${this.esc(displayName)} <span style="opacity:.6">${c.count}</span></button>`;
            });
            html += '</div>';
        }

        if (data.pictures && data.pictures.length > 0) {
            html += `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;color:var(--text-secondary);font-size:13px">
                <span>${data.total} pictures${this.picturesCategory ? ' in ' + this.esc(this.picturesCategory || 'Uncategorized') : ''}</span>
                <span>Page ${this.picturesPage} of ${totalPages}</span>
            </div>`;

            html += '<div class="pictures-grid">';
            data.pictures.forEach(pic => {
                const thumbSrc = pic.thumbnailPath ? `/picthumb/${pic.thumbnailPath}` : `/api/picthumb/${pic.id}`;
                html += `<div class="picture-card" onclick="App.openPictureViewer(${pic.id})" data-pic-id="${pic.id}">
                    <div class="picture-card-thumb">
                        <img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                        <span class="picture-card-placeholder" style="display:none">&#128247;</span>
                    </div>
                    <div class="picture-card-info">
                        <div class="picture-card-name">${this.esc(pic.fileName)}</div>
                        <div class="picture-card-meta">${pic.width}x${pic.height} &middot; ${this.formatSize(pic.sizeBytes)}</div>
                    </div>
                </div>`;
            });
            html += '</div>';
            html += this.renderPicturesPagination(this.picturesPage, totalPages);
        } else {
            html += this.emptyState(this.t('empty.noPictures.title'), this.t('empty.noPictures.desc'));
        }

        target.innerHTML = html;
    },

    changePicturesSort(sort) {
        this.picturesSort = sort;
        this.picturesPage = 1;
        this.loadPicturesPage();
    },

    filterPictureCategory(category) {
        this.picturesCategory = category;
        this.picturesPage = 1;
        this.loadPicturesPage();
    },

    goPicturesPage(page) {
        const totalPages = Math.ceil(this.picturesTotal / this.picturesPerPage);
        if (page < 1 || page > totalPages) return;
        this.picturesPage = page;
        this.loadPicturesPage();
        document.getElementById('main-content').scrollTop = 0;
    },

    renderPicturesPagination(currentPage, totalPages) {
        if (totalPages <= 1) return '';
        let html = '<div class="pagination">';
        html += `<button class="page-btn${currentPage === 1 ? ' disabled' : ''}" onclick="App.goPicturesPage(${currentPage - 1})">&laquo; ${this.t('pagination.prev')}</button>`;
        const pages = [];
        pages.push(1);
        let rangeStart = Math.max(2, currentPage - 2);
        let rangeEnd = Math.min(totalPages - 1, currentPage + 2);
        if (rangeStart > 2) pages.push('...');
        for (let p = rangeStart; p <= rangeEnd; p++) pages.push(p);
        if (rangeEnd < totalPages - 1) pages.push('...');
        if (totalPages > 1) pages.push(totalPages);
        pages.forEach(p => {
            if (p === '...') html += '<span class="page-ellipsis">...</span>';
            else html += `<button class="page-btn${p === currentPage ? ' active' : ''}" onclick="App.goPicturesPage(${p})">${p}</button>`;
        });
        html += `<button class="page-btn${currentPage === totalPages ? ' disabled' : ''}" onclick="App.goPicturesPage(${currentPage + 1})">${this.t('pagination.next')} &raquo;</button>`;
        html += '</div>';
        return html;
    },

    // ─── Picture Viewer Modal ────────────────────────────
    _picAutoplayTimer: null,
    _picAutoplay: false,
    _picIds: [],
    _picCurrentIndex: -1,
    _picCurrentMeta: null,

    _buildPicDetailRows(metadata) {
        return [
            `<div class="setting-row setting-row-col"><span class="setting-label">Filename</span><span class="setting-value" style="word-break:break-all;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;line-height:1.4">${this.esc(metadata.fileName)}</span></div>`,
            `<div class="setting-row"><span class="setting-label">Dimensions</span><span class="setting-value">${metadata.width} x ${metadata.height}</span></div>`,
            `<div class="setting-row"><span class="setting-label">Size</span><span class="setting-value">${this.formatSize(metadata.sizeBytes)}</span></div>`,
            `<div class="setting-row"><span class="setting-label">Format</span><span class="setting-value">${metadata.format}</span></div>`,
            metadata.category ? `<div class="setting-row"><span class="setting-label">Category</span><span class="setting-value">${this.esc(metadata.category)}</span></div>` : '',
            metadata.dateTaken ? `<div class="setting-row"><span class="setting-label">Date Taken</span><span class="setting-value">${new Date(metadata.dateTaken).toLocaleString()}</span></div>` : '',
            metadata.cameraMake ? `<div class="setting-row"><span class="setting-label">Camera</span><span class="setting-value">${this.esc(metadata.cameraMake)}${metadata.cameraModel ? ' ' + this.esc(metadata.cameraModel) : ''}</span></div>` : '',
            metadata.lensModel ? `<div class="setting-row"><span class="setting-label">Lens</span><span class="setting-value">${this.esc(metadata.lensModel)}</span></div>` : '',
            metadata.focalLength ? `<div class="setting-row"><span class="setting-label">Focal Length</span><span class="setting-value">${metadata.focalLength}</span></div>` : '',
            metadata.fNumber ? `<div class="setting-row"><span class="setting-label">Aperture</span><span class="setting-value">${metadata.fNumber}</span></div>` : '',
            metadata.exposureTime ? `<div class="setting-row"><span class="setting-label">Exposure</span><span class="setting-value">${metadata.exposureTime}</span></div>` : '',
            metadata.isoSpeed ? `<div class="setting-row"><span class="setting-label">ISO</span><span class="setting-value">${metadata.isoSpeed}</span></div>` : '',
            metadata.flash ? `<div class="setting-row"><span class="setting-label">Flash</span><span class="setting-value">${metadata.flash}</span></div>` : '',
            metadata.software ? `<div class="setting-row"><span class="setting-label">Software</span><span class="setting-value">${this.esc(metadata.software)}</span></div>` : '',
            metadata.dpiX ? `<div class="setting-row"><span class="setting-label">DPI</span><span class="setting-value">${metadata.dpiX} x ${metadata.dpiY}</span></div>` : '',
            `<div class="setting-row"><span class="setting-label">Added</span><span class="setting-value">${new Date(metadata.dateAdded).toLocaleString()}</span></div>`
        ].filter(Boolean).join('');
    },

    _updatePicNav() {
        const currentIndex = this._picCurrentIndex;
        const picIds = this._picIds;
        const container = document.getElementById('pic-image-area');
        if (!container) return;
        // Update prev/next buttons
        const prevBtn = container.querySelector('.picture-nav-prev');
        const nextBtn = container.querySelector('.picture-nav-next');
        if (prevBtn) prevBtn.style.display = currentIndex > 0 ? '' : 'none';
        if (nextBtn) nextBtn.style.display = currentIndex < picIds.length - 1 ? '' : 'none';
    },

    async openPictureViewer(pictureId) {
        const metadata = await this.api(`pictures/${pictureId}/metadata`);
        if (!metadata) return;
        this._picCurrentMeta = metadata;

        // Build pic list only on first open (not during navigation)
        const existingModal = document.querySelector('.picture-modal-overlay');
        if (!existingModal) {
            const cards = document.querySelectorAll('.picture-card');
            this._picIds = Array.from(cards).map(c => parseInt(c.dataset.picId));
        }
        this._picCurrentIndex = this._picIds.indexOf(pictureId);
        const currentIndex = this._picCurrentIndex;
        const picIds = this._picIds;

        // If modal already open, update in-place (no flash, keeps fullscreen)
        if (existingModal) {
            const img = document.getElementById('picture-modal-img');
            if (img) {
                img.src = `/api/image/${metadata.id}`;
                img.alt = this.esc(metadata.fileName);
                img.classList.remove('zoomed');
            }
            const title = existingModal.querySelector('.picture-modal-title');
            if (title) title.textContent = metadata.fileName;
            const metaBody = document.getElementById('pic-metadata-body');
            if (metaBody) metaBody.innerHTML = this._buildPicDetailRows(metadata);
            this._updatePicNav();
            // Restart autoplay timer to reset the 3s countdown
            if (this._picAutoplay) this._startPicAutoplay();
            // Update keyboard handler
            if (this._pictureModalKeyHandler) document.removeEventListener('keydown', this._pictureModalKeyHandler);
            this._pictureModalKeyHandler = (e) => {
                if (e.key === 'Escape') this.closePictureViewer();
                if (e.key === 'ArrowLeft' && this._picCurrentIndex > 0) this.openPictureViewer(this._picIds[this._picCurrentIndex - 1]);
                if (e.key === 'ArrowRight' && this._picCurrentIndex < this._picIds.length - 1) this.openPictureViewer(this._picIds[this._picCurrentIndex + 1]);
            };
            document.addEventListener('keydown', this._pictureModalKeyHandler);
            return;
        }

        // First open: build the full modal
        const detailRows = this._buildPicDetailRows(metadata);

        let html = `<div class="picture-modal-overlay" onclick="App.closePictureViewer(event)">
            <div class="picture-modal" onclick="event.stopPropagation()">
                <div class="picture-modal-header">
                    <span class="picture-modal-title">${this.esc(metadata.fileName)}</span>
                    <div class="picture-modal-actions">
                        <span class="pic-autoplay-indicator${this._picAutoplay ? ' active' : ''}" id="pic-autoplay-label">Autoplay 3s</span>
                        <button class="pic-action-btn${this._picAutoplay ? ' active' : ''}" id="pic-btn-autoplay" title="Autoplay (3s)" onclick="App.togglePicAutoplay()">
                            <svg><use href="#icon-play"/></svg>
                        </button>
                        <button class="pic-action-btn" title="Full Screen" onclick="App.picFullScreen()">
                            <svg><use href="#icon-maximize"/></svg>
                        </button>
                        <button class="pic-action-btn" title="Download" onclick="App.picDownload()">
                            <svg><use href="#icon-download"/></svg>
                        </button>
                        <button class="pic-action-btn" id="pic-btn-details" title="Image Details" onclick="App.togglePicDetails()">
                            <svg><use href="#icon-list"/></svg>
                        </button>
                    </div>
                    <button class="picture-modal-close" onclick="App.closePictureViewer()">&times;</button>
                </div>
                <div class="picture-modal-body">
                    <div class="picture-modal-image-container" id="pic-image-area">
                        <button class="picture-nav-btn picture-nav-prev" onclick="event.stopPropagation(); App.picGoPrev()" style="${currentIndex > 0 ? '' : 'display:none'}">&lsaquo;</button>
                        <img src="/api/image/${metadata.id}" class="picture-modal-image" id="picture-modal-img" alt="${this.esc(metadata.fileName)}"
                             ondblclick="this.classList.toggle('zoomed')">
                        <button class="picture-nav-btn picture-nav-next" onclick="event.stopPropagation(); App.picGoNext()" style="${currentIndex < picIds.length - 1 ? '' : 'display:none'}">&rsaquo;</button>
                    </div>
                    <div class="picture-modal-sidebar collapsed" id="pic-sidebar">
                        <div class="pic-details-toggle" onclick="App.togglePicDetailsPanel()">
                            <span class="pic-details-arrow collapsed" id="pic-details-arrow">
                                <svg><use href="#icon-chevron-down"/></svg>
                            </span>
                            <h3>Image Details</h3>
                        </div>
                        <div class="picture-modal-metadata" id="pic-metadata-body">
                            ${detailRows}
                        </div>
                    </div>
                </div>
            </div>
        </div>`;

        document.body.insertAdjacentHTML('beforeend', html);

        if (this._picAutoplay) this._startPicAutoplay();

        if (this._pictureModalKeyHandler) {
            document.removeEventListener('keydown', this._pictureModalKeyHandler);
        }
        this._pictureModalKeyHandler = (e) => {
            if (e.key === 'Escape') this.closePictureViewer();
            if (e.key === 'ArrowLeft' && this._picCurrentIndex > 0) this.openPictureViewer(this._picIds[this._picCurrentIndex - 1]);
            if (e.key === 'ArrowRight' && this._picCurrentIndex < this._picIds.length - 1) this.openPictureViewer(this._picIds[this._picCurrentIndex + 1]);
        };
        document.addEventListener('keydown', this._pictureModalKeyHandler);
    },

    picGoPrev() {
        if (this._picCurrentIndex > 0) this.openPictureViewer(this._picIds[this._picCurrentIndex - 1]);
    },

    picGoNext() {
        if (this._picCurrentIndex < this._picIds.length - 1) this.openPictureViewer(this._picIds[this._picCurrentIndex + 1]);
    },

    togglePicDetails() {
        const sidebar = document.getElementById('pic-sidebar');
        if (sidebar) sidebar.classList.toggle('collapsed');
    },

    togglePicDetailsPanel() {
        const arrow = document.getElementById('pic-details-arrow');
        const body = document.getElementById('pic-metadata-body');
        if (arrow) arrow.classList.toggle('collapsed');
        if (body) {
            const isCollapsed = body.style.display === 'none';
            body.style.display = isCollapsed ? '' : 'none';
        }
    },

    picFullScreen() {
        const container = document.getElementById('pic-image-area');
        if (!container) return;
        if (document.fullscreenElement) {
            document.exitFullscreen();
        } else {
            container.requestFullscreen().catch(() => {});
        }
    },

    picDownload() {
        if (!this._picCurrentMeta) return;
        const a = document.createElement('a');
        a.href = `/api/image/${this._picCurrentMeta.id}`;
        a.download = this._picCurrentMeta.fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    },

    togglePicAutoplay() {
        this._picAutoplay = !this._picAutoplay;
        const btn = document.getElementById('pic-btn-autoplay');
        const label = document.getElementById('pic-autoplay-label');
        if (btn) btn.classList.toggle('active', this._picAutoplay);
        if (label) label.classList.toggle('active', this._picAutoplay);
        if (this._picAutoplay) {
            this._startPicAutoplay();
        } else {
            this._stopPicAutoplay();
        }
    },

    _startPicAutoplay() {
        this._stopPicAutoplay();
        this._picAutoplayTimer = setInterval(() => {
            if (this._picCurrentIndex < this._picIds.length - 1) {
                this.openPictureViewer(this._picIds[this._picCurrentIndex + 1]);
            } else {
                this.openPictureViewer(this._picIds[0]);
            }
        }, 3000);
    },

    _stopPicAutoplay() {
        if (this._picAutoplayTimer) {
            clearInterval(this._picAutoplayTimer);
            this._picAutoplayTimer = null;
        }
    },

    closePictureViewer(event) {
        if (event && event.target !== event.currentTarget) return;
        this._stopPicAutoplay();
        this._picAutoplay = false;
        document.querySelector('.picture-modal-overlay')?.remove();
        if (this._pictureModalKeyHandler) {
            document.removeEventListener('keydown', this._pictureModalKeyHandler);
            this._pictureModalKeyHandler = null;
        }
    },

    // ─── Track Table Renderer ────────────────────────────────
    renderTrackTable(tracks, showTrackNum = false) {
        return `<table class="track-list"><thead><tr>
            ${showTrackNum ? `<th class="track-number">${this.t('table.trackNum')}</th>` : '<th class="track-number"></th>'}
            <th>${this.t('table.title')}</th><th>${this.t('table.artist')}</th><th>${this.t('table.album')}</th><th class="track-duration">${this.t('table.duration')}</th>
            <th class="track-actions"></th>
        </tr></thead><tbody>${this.renderTrackRows(tracks, showTrackNum)}</tbody></table>`;
    },

    renderTrackRows(tracks, showTrackNum = false) {
        if (!tracks) return '';
        return tracks.map((t, i) => {
            const dur = this.formatDuration(t.duration);
            const fmt = this.trackFormat(t);
            const num = showTrackNum ? (t.trackNumber || i + 1) : (i + 1);
            const favClass = t.isFavourite ? 'active' : '';
            return `<tr onclick="App.playFromList(${i})" data-track-id="${t.id}">
                <td class="track-number">${num}</td>
                <td class="track-title">${this.esc(t.title)}${fmt ? `<span class="track-format-badge ${this.trackFormatClass(fmt)}">${fmt}</span>` : ''}</td>
                <td>${this.esc(t.artist)}</td>
                <td>${this.esc(t.album)}</td>
                <td class="track-duration">${dur}</td>
                <td class="track-actions">
                    <button class="track-fav-btn ${favClass}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                    <button class="track-fav-btn" onclick="App.showAddToPlaylistPopup(${t.id}, this)" title="Add to playlist" style="color:var(--text-muted)">+</button>
                    <button class="track-fav-btn" onclick="event.stopPropagation(); App.showTrackMenu(${t.id}, event)" title="More options" style="color:var(--text-muted);font-size:18px;font-weight:700;letter-spacing:1px">&#8942;</button>
                </td>
            </tr>`;
        }).join('');
    },

    // ─── Audio Player ────────────────────────────────────────
    bindPlayer() {
        const audio = this.audioPlayer;
        document.getElementById('btn-play').addEventListener('click', () => this.togglePlay());
        document.getElementById('btn-prev').addEventListener('click', () => this.prevTrack());
        document.getElementById('btn-next').addEventListener('click', () => this.nextTrack());
        document.getElementById('btn-shuffle').addEventListener('click', () => {
            this.shuffle = !this.shuffle;
            document.getElementById('btn-shuffle').style.color = this.shuffle ? 'var(--accent)' : '';
        });
        document.getElementById('btn-repeat').addEventListener('click', () => {
            const modes = ['off', 'all', 'one'];
            this.repeat = modes[(modes.indexOf(this.repeat) + 1) % 3];
            const btn = document.getElementById('btn-repeat');
            btn.style.color = this.repeat !== 'off' ? 'var(--accent)' : '';
            btn.title = `Repeat: ${this.repeat}`;
        });
        document.getElementById('volume-slider').addEventListener('input', (e) => { audio.volume = e.target.value / 100; });
        audio.volume = 0.8;

        // EQ button
        document.getElementById('btn-eq').addEventListener('click', () => this.toggleEQPanel());

        // Favourite button
        document.getElementById('btn-player-fav').addEventListener('click', () => this.togglePlayerFav());

        // Add to Playlist button
        document.getElementById('btn-player-add-playlist').addEventListener('click', (e) => {
            e.stopPropagation();
            this.togglePlaylistDropdown();
        });
        document.getElementById('player-playlist-dropdown').addEventListener('click', (e) => {
            e.stopPropagation();
        });
        document.addEventListener('click', () => {
            document.getElementById('player-playlist-dropdown')?.classList.remove('open');
        });

        // Lyrics button
        document.getElementById('btn-player-lyrics').addEventListener('click', () => this.toggleLyrics());

        document.getElementById('progress-bar').addEventListener('click', (e) => {
            const rect = e.currentTarget.getBoundingClientRect();
            audio.currentTime = ((e.clientX - rect.left) / rect.width) * audio.duration;
        });
        audio.addEventListener('timeupdate', () => {
            if (!audio.duration) return;
            const pct = ((audio.currentTime / audio.duration) * 100) + '%';
            document.getElementById('progress-fill').style.width = pct;
            const mobileFill = document.getElementById('mobile-progress-fill');
            if (mobileFill) mobileFill.style.width = pct;
            document.getElementById('time-current').textContent = this.formatDuration(audio.currentTime);
            document.getElementById('time-total').textContent = this.formatDuration(audio.duration);
            // Update podcast now-playing panel seek bar
            const pnpFill = document.getElementById('pnp-fill');
            const pnpThumb = document.getElementById('pnp-thumb');
            const pnpCur = document.getElementById('pnp-cur');
            const pnpTot = document.getElementById('pnp-tot');
            if (pnpFill) pnpFill.style.width = pct;
            if (pnpThumb) pnpThumb.style.left = pct;
            if (pnpCur) pnpCur.textContent = this.formatDuration(audio.currentTime);
            if (pnpTot) pnpTot.textContent = this.formatDuration(audio.duration);
        });
        audio.addEventListener('ended', () => {
            if (this.isRadioPlaying) {
                // Radio streams shouldn't end - try to reconnect
                setTimeout(() => {
                    if (this.isRadioPlaying && this.currentRadioStation) {
                        this.audioPlayer.src = this.currentRadioStation.streamUrl;
                        this.audioPlayer.play();
                    }
                }, 2000);
                return;
            }
            if (this.repeat === 'one') { audio.currentTime = 0; audio.play(); }
            else this.nextTrack();
        });
        audio.addEventListener('error', () => {
            if (this.isRadioPlaying) {
                document.getElementById('player-artist').innerHTML =
                    '<span style="color:var(--danger)">Stream unavailable - retrying...</span>';
                setTimeout(() => {
                    if (this.isRadioPlaying && this.currentRadioStation) {
                        this.audioPlayer.src = this.currentRadioStation.streamUrl;
                        this.audioPlayer.play().catch(() => {});
                    }
                }, 3000);
            }
        });
    },

    stopAllMedia() {
        // Cancel any pending "Up Next" countdown
        if (this._upNextTimer) { clearInterval(this._upNextTimer); this._upNextTimer = null; }
        // Stop video progress tracking
        this._stopVideoProgressTracking();
        // Stop audio player and hide player bar
        if (this.audioPlayer && this.audioPlayer.src) {
            this.audioPlayer.pause();
            this.audioPlayer.src = '';
        }
        this.isPlaying = false;
        this.currentTrack = null;
        const playerBar = document.getElementById('player-bar');
        if (playerBar) playerBar.classList.add('player-hidden');
        // Stop any video players on the page
        document.querySelectorAll('video').forEach(v => { v.pause(); v.removeAttribute('src'); v.load(); });
        // Kill any active FFmpeg transcode process
        this.stopCurrentTranscode();
    },

    playTrack(track) {
        if (!track) return;
        // If radio was playing, clean up radio state
        if (this.isRadioPlaying) {
            this.isRadioPlaying = false;
            this.currentRadioStation = null;
            document.getElementById('player-bar').classList.remove('radio-mode');
            document.getElementById('btn-player-fav').style.display = '';
            document.getElementById('btn-player-add-playlist').style.display = '';
            document.getElementById('progress-bar').style.display = '';
            document.getElementById('btn-prev').style.display = '';
            document.getElementById('btn-next').style.display = '';
            document.getElementById('btn-shuffle').style.display = '';
            document.getElementById('btn-repeat').style.display = '';
            document.getElementById('btn-eq').style.display = '';
        }
        // If an audiobook was playing, restore full player controls
        if (this.isAudioBookPlaying) {
            this.isAudioBookPlaying = false;
            this._currentAudioBookId = null;
            document.getElementById('btn-player-fav').style.display = '';
            document.getElementById('btn-player-add-playlist').style.display = '';
            document.getElementById('btn-player-lyrics').style.display = '';
            document.getElementById('btn-prev').style.display = '';
            document.getElementById('btn-next').style.display = '';
            document.getElementById('btn-shuffle').style.display = '';
            document.getElementById('btn-repeat').style.display = '';
            document.getElementById('btn-eq').style.display = '';
        }
        // Stop any playing video before starting audio
        document.querySelectorAll('video').forEach(v => { v.pause(); v.removeAttribute('src'); v.load(); });
        this.currentTrack = track;
        this.audioPlayer.src = `/api/stream/${track.id}`;
        this.initEqualizer();
        this.audioPlayer.play();
        this.isPlaying = true;
        document.getElementById('player-bar').classList.remove('player-hidden');
        document.getElementById('btn-eq').style.display = '';
        document.getElementById('btn-play').innerHTML = '<svg style="width:22px;height:22px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-pause"/></svg>';
        document.getElementById('player-title').textContent = track.title || 'Unknown';
        document.getElementById('player-artist').textContent = track.artist || '';
        const coverDiv = document.getElementById('player-cover');
        if (track.hasAlbumArt) {
            coverDiv.innerHTML = `<img src="/api/cover/track/${track.id}" onerror="this.parentElement.innerHTML='<div style=\\'display:flex;align-items:center;justify-content:center;width:100%;height:100%;font-size:22px;color:#666\\'>&#9835;</div>'" alt="">`;
        } else {
            coverDiv.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;width:100%;height:100%;font-size:22px;color:#666">&#9835;</div>';
        }
        // Update fav button state
        const favBtn = document.getElementById('btn-player-fav');
        if (favBtn) favBtn.classList.toggle('active', !!track.isFavourite);
        // Close playlist dropdown
        document.getElementById('player-playlist-dropdown')?.classList.remove('open');
        document.querySelectorAll('.track-list tr.playing').forEach(r => r.classList.remove('playing'));
        const row = document.querySelector(`tr[data-track-id="${track.id}"]`);
        if (row) row.classList.add('playing');
        if (this._ncActive) this._ncUpdateSong();
    },

    playFromList(index) {
        const rows = document.querySelectorAll('.track-list tbody tr');
        if (rows.length > 0) {
            this.playlist = Array.from(rows).map(row => {
                const cells = row.querySelectorAll('td');
                return { id: parseInt(row.dataset.trackId), title: cells[1]?.textContent || '', artist: cells[2]?.textContent || '', album: cells[3]?.textContent || '', hasAlbumArt: true };
            });
        }
        this.playIndex = index;
        if (this.playlist[index]) this.playTrack(this.playlist[index]);
    },

    stopPlayer() {
        this.audioPlayer.pause();
        this.audioPlayer.src = '';
        this.isPlaying = false;
        this.currentTrack = null;
        this._currentPodcastEp = null;
        this.playlist = [];
        this.playIndex = -1;
        if (this._podcastPositionInterval) {
            clearInterval(this._podcastPositionInterval);
            this._podcastPositionInterval = null;
        }
        const bar = document.getElementById('player-bar');
        bar.classList.add('player-hidden');
        bar.classList.remove('podcast-mode');
        const npPanel = document.getElementById('podcast-now-playing');
        if (npPanel) npPanel.className = 'podcast-np-hidden';
    },

    togglePlay() {
        if (!this.audioPlayer.src) return;
        const svgStyle = 'width:22px;height:22px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round';
        const pnpStyle = 'width:24px;height:24px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round';
        const icon = this.isPlaying ? 'play' : 'pause';
        if (this.isPlaying) { this.audioPlayer.pause(); this.isPlaying = false; }
        else { this.audioPlayer.play(); this.isPlaying = true; }
        document.getElementById('btn-play').innerHTML = `<svg style="${svgStyle}"><use href="#icon-${icon}"/></svg>`;
        const pnpBtn = document.getElementById('pnp-play-btn');
        if (pnpBtn) pnpBtn.innerHTML = `<svg style="${pnpStyle}"><use href="#icon-${icon}"/></svg>`;
        if (this._ncActive) {
            const ncPlay = document.getElementById('nc-btn-play');
            if (ncPlay) ncPlay.textContent = this.isPlaying ? '\u23F8' : '\u25B6';
        }
    },

    async nextTrack() {
        if (this.playlist.length === 0) return;
        if (this.shuffle) {
            if (this.playlist.length <= 2) {
                const track = await this.api('tracks/random');
                if (track && track.id) {
                    this.playlist = [track];
                    this.playIndex = 0;
                    this.playTrack(track);
                    return;
                }
            }
            this.playIndex = Math.floor(Math.random() * this.playlist.length);
        } else {
            this.playIndex++;
            if (this.playIndex >= this.playlist.length) { if (this.repeat === 'all') this.playIndex = 0; else return; }
        }
        this.playTrack(this.playlist[this.playIndex]);
    },

    prevTrack() {
        if (this.playlist.length === 0) return;
        if (this.audioPlayer.currentTime > 3) { this.audioPlayer.currentTime = 0; return; }
        this.playIndex--;
        if (this.playIndex < 0) this.playIndex = this.repeat === 'all' ? this.playlist.length - 1 : 0;
        this.playTrack(this.playlist[this.playIndex]);
    },

    // ─── Favourites Toggle ───────────────────────────────────
    async toggleFav(trackId, btn) {
        const result = await this.apiPost(`tracks/${trackId}/favourite`);
        if (result) btn.classList.toggle('active', result.isFavourite);
    },

    async toggleMvFav(mvId, btn) {
        const result = await this.apiPost(`musicvideos/${mvId}/favourite`);
        if (result) btn.classList.toggle('active', result.isFavourite);
    },

    async toggleVideoFav(videoId, btn) {
        const result = await this.apiPost(`videos/${videoId}/favourite`);
        if (result) btn.classList.toggle('active', result.isFavourite);
    },

    async toggleAudioBookFav(id, btn) {
        const result = await this.apiPost(`audiobooks/${id}/favourite`);
        if (result) {
            btn.classList.toggle('active', result.isFavourite);
            // Sync player fav button if this audiobook is currently playing
            if (this.isAudioBookPlaying && this._currentAudioBookId === id) {
                document.getElementById('btn-player-fav').classList.toggle('active', result.isFavourite);
            }
            // If on favorites page and unfav'd, remove the card
            if (!result.isFavourite && this.currentPage === 'favourites') {
                btn.closest('[data-audiobook-id]')?.remove();
            }
        }
    },

    async toggleRadioFav(stationId, btn) {
        const result = await this.apiPost(`radio/${stationId}/favourite`);
        if (result) {
            btn.classList.toggle('active', result.isFavourite);
            // Update local state
            const station = this.radioStations.find(s => s.id === stationId);
            if (station) station.isFavourite = result.isFavourite;
        }
    },

    // ─── Player Favourite ────────────────────────────────────
    async togglePlayerFav() {
        // Handle audiobook fav from player bar
        if (this.isAudioBookPlaying && this._currentAudioBookId) {
            const result = await this.apiPost(`audiobooks/${this._currentAudioBookId}/favourite`);
            if (result) {
                document.getElementById('btn-player-fav').classList.toggle('active', result.isFavourite);
                // Sync heart on visible card or detail page
                const card = document.querySelector(`[data-audiobook-id="${this._currentAudioBookId}"] .song-card-fav`);
                if (card) card.classList.toggle('active', result.isFavourite);
            }
            return;
        }
        if (!this.currentTrack) return;
        const result = await this.apiPost(`tracks/${this.currentTrack.id}/favourite`);
        if (result) {
            this.currentTrack.isFavourite = result.isFavourite;
            document.getElementById('btn-player-fav').classList.toggle('active', result.isFavourite);
            // Sync heart on song card if visible
            const card = document.querySelector(`.song-card[data-track-id="${this.currentTrack.id}"] .song-card-fav`);
            if (card) card.classList.toggle('active', result.isFavourite);
            const row = document.querySelector(`tr[data-track-id="${this.currentTrack.id}"] .track-fav-btn`);
            if (row) row.classList.toggle('active', result.isFavourite);
        }
    },

    // ─── Player Add to Playlist ──────────────────────────────
    async togglePlaylistDropdown() {
        if (!this.currentTrack) return;
        const dd = document.getElementById('player-playlist-dropdown');
        if (dd.classList.contains('open')) { dd.classList.remove('open'); return; }
        dd.innerHTML = '<div class="pl-dropdown-title">Add to playlist</div><div class="pl-dropdown-empty">Loading...</div>';
        dd.classList.add('open');
        const playlists = await this.api('playlists');
        let inner = '<div class="pl-dropdown-title">Add to playlist</div>';
        if (playlists && playlists.length > 0) {
            playlists.forEach(p => {
                inner += `<div class="pl-dropdown-item" onclick="event.stopPropagation(); App.addToPlaylistFromPlayer(${p.id}, this)">${this.esc(p.name)} <span style="opacity:.5;font-size:11px">(${p.trackCount})</span></div>`;
            });
        } else {
            inner += '<div class="pl-dropdown-empty">No playlists yet</div>';
        }
        dd.innerHTML = inner;
    },

    async addToPlaylistFromPlayer(playlistId, el) {
        if (!this.currentTrack) return;
        const origText = el ? el.textContent : '';
        if (el) { el.style.pointerEvents = 'none'; el.textContent = 'Adding...'; }
        const result = await this.apiPost(`playlists/${playlistId}/tracks`, { trackId: this.currentTrack.id });
        if (result && result.message && el) {
            el.classList.add('pl-added');
            el.innerHTML = '\u2714 Added!';
            setTimeout(() => {
                document.getElementById('player-playlist-dropdown')?.classList.remove('open');
            }, 1000);
        } else if (el) {
            el.textContent = origText;
            el.style.pointerEvents = '';
        }
    },

    // ─── Search ──────────────────────────────────────────────
    bindSearch() {
        let timeout;
        const input = document.getElementById('global-search');
        input.addEventListener('input', (e) => {
            clearTimeout(timeout);
            const q = e.target.value.trim();
            if (q.length < 2) return;
            timeout = setTimeout(() => this.performSearch(q), 300);
        });
        input.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') { const q = e.target.value.trim(); if (q.length >= 2) this.performSearch(q); }
        });
    },

    async performSearch(query) {
        const data = await this.api(`search?q=${encodeURIComponent(query)}`);
        if (!data) return;
        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>Search: "${this.esc(query)}"</span>`;

        // Deduplicate videos (TV/anime series count as one) for accurate result count
        const _seenForCount = new Set();
        const _uniqueVideos = (data.videos || []).filter(v => {
            if ((v.mediaType === 'tv' || v.mediaType === 'anime') && v.seriesName) {
                if (_seenForCount.has(v.seriesName)) return false;
                _seenForCount.add(v.seriesName);
            }
            return true;
        });
        let totalResults = (data.artists?.length || 0) + (data.albums?.length || 0)
            + (data.tracks?.length || 0) + (data.pictures?.length || 0)
            + (data.ebooks?.length || 0) + (data.musicVideos?.length || 0)
            + _uniqueVideos.length + (data.actors?.length || 0);

        let html = `<div class="page-header"><h1>Search Results</h1>
            <span style="color:var(--text-secondary);font-size:13px">${totalResults} result${totalResults !== 1 ? 's' : ''}</span>
        </div>`;

        // Artists
        if (data.artists && data.artists.length > 0) {
            html += '<div class="section-title">Artists</div><div class="card-grid">';
            data.artists.forEach(a => {
                const imgHtml = a.imagePath
                    ? `<img src="/singerphoto/${a.imagePath}" class="artist-portrait" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><div class="artist-portrait-fallback" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-music"/></svg></div>`
                    : `<div class="artist-portrait-fallback"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-music"/></svg></div>`;
                html += `<div class="card" onclick="App.openArtist('${this.esc(a.name).replace(/'/g,"\\'")}')"><div class="card-cover artist-cover">${imgHtml}</div><div class="card-info"><div class="card-title">${this.esc(a.name)}</div><div class="card-subtitle">${a.albumCount || 0} ${this.t('label.albums','albums')} &middot; ${a.trackCount || 0} ${this.t('label.tracks','tracks')}</div></div></div>`;
            });
            html += '</div>';
        }
        // Albums
        if (data.albums && data.albums.length > 0) {
            html += '<div class="section-title">Albums</div><div class="card-grid">';
            data.albums.forEach(a => { html += `<div class="card" onclick="App.openAlbum(${a.id})"><div class="card-cover"><img src="/api/cover/${a.id}" onerror="this.style.display='none';this.parentElement.innerHTML='<div class=placeholder-icon>&#128191;</div>'" alt=""></div><div class="card-info"><div class="card-title">${this.esc(a.title)}</div><div class="card-subtitle">${this.esc(a.artist)}</div></div></div>`; });
            html += '</div>';
        }
        // Tracks
        if (data.tracks && data.tracks.length > 0) {
            html += '<div class="section-title">Tracks</div>';
            html += this.renderTrackTable(data.tracks);
        }
        // Music Videos
        if (data.musicVideos && data.musicVideos.length > 0) {
            html += '<div class="section-title">Music Videos</div><div class="mv-grid">';
            data.musicVideos.forEach(v => {
                const thumbSrc = v.thumbnailPath ? `/mvthumb/${v.thumbnailPath}` : '';
                const dur = this.formatDuration(v.duration);
                html += `<div class="mv-card" onclick="App.openMvDetail(${v.id})">
                    <div class="mv-card-thumb">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none">&#127909;</span>`
                            : `<span class="mv-card-placeholder">&#127909;</span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        <span class="mv-format-badge">${this.esc(v.format)}</span>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${this.esc(v.artist)}</div>
                    </div>
                </div>`;
            });
            html += '</div>';
        }
        // eBooks
        if (data.ebooks && data.ebooks.length > 0) {
            html += '<div class="section-title">eBooks</div><div class="ebooks-grid">';
            data.ebooks.forEach(book => {
                const formatBadge = book.format ? book.format.toLowerCase() : 'epub';
                const author = book.author || 'Unknown Author';
                html += `<div class="ebook-card" onclick="App.openEBookDetail(${book.id})">
                    <div class="ebook-card-cover">
                        ${book.coverImage
                            ? `<img src="/ebookcover/${book.coverImage}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="ebook-card-placeholder" style="display:none">&#128214;</span>`
                            : `<span class="ebook-card-placeholder">&#128214;</span>`}
                        <span class="ebook-format-badge ebook-format-${formatBadge}">${book.format}</span>
                    </div>
                    <div class="ebook-card-info">
                        <div class="ebook-card-title">${this.esc(book.title)}</div>
                        <div class="ebook-card-author">${this.esc(author)}</div>
                        <div class="ebook-card-meta">
                            <span>${book.pageCount > 0 ? book.pageCount + ' pages' : book.format}</span>
                            <span>${this.formatSize(book.fileSize)}</span>
                        </div>
                    </div>
                </div>`;
            });
            html += '</div>';
        }
        // Pictures
        if (data.pictures && data.pictures.length > 0) {
            html += '<div class="section-title">Pictures</div><div class="pictures-grid">';
            data.pictures.forEach(pic => {
                const thumbSrc = pic.thumbnailPath ? `/picthumb/${pic.thumbnailPath}` : `/api/picthumb/${pic.id}`;
                html += `<div class="picture-card" onclick="App.openPictureViewer(${pic.id})" data-pic-id="${pic.id}">
                    <div class="picture-card-thumb">
                        <img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                        <span class="picture-card-placeholder" style="display:none">&#128247;</span>
                    </div>
                    <div class="picture-card-info">
                        <div class="picture-card-name">${this.esc(pic.fileName)}</div>
                        <div class="picture-card-meta">${pic.width}x${pic.height} &middot; ${this.formatSize(pic.sizeBytes)}</div>
                    </div>
                </div>`;
            });
            html += '</div>';
        }

        // Actors
        if (data.actors && data.actors.length > 0) {
            html += `<div class="section-title">${this.t('page.actors')}</div><div style="display:flex;flex-wrap:wrap;gap:16px;margin-bottom:20px">`;
            data.actors.forEach(a => {
                const photo = a.imageCached ? `/actorphoto/${a.imageCached}` : '';
                html += `<div style="text-align:center;width:100px;cursor:pointer" onclick="App.openActorDetail(${a.id})">
                    <div style="width:80px;height:80px;border-radius:50%;overflow:hidden;background:var(--bg-card);margin:0 auto 6px;display:flex;align-items:center;justify-content:center;border:2px solid var(--border-color)">
                        ${photo
                            ? `<img src="${photo}" style="width:100%;height:100%;object-fit:cover" alt="">`
                            : `<svg style="width:32px;height:32px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-users"/></svg>`}
                    </div>
                    <div style="font-size:12px;font-weight:500;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${this.esc(a.name)}</div>
                    <div style="font-size:11px;color:var(--text-secondary)">${a.movieCount} ${this.t('actors.movies')}</div>
                </div>`;
            });
            html += '</div>';
        }
        // Movies / TV Shows
        if (data.videos && data.videos.length > 0) {
            // Deduplicate by seriesName for TV shows and anime series
            const seen = new Set();
            const unique = data.videos.filter(v => {
                if ((v.mediaType === 'tv' || v.mediaType === 'anime') && v.seriesName) {
                    if (seen.has(v.seriesName)) return false;
                    seen.add(v.seriesName);
                }
                return true;
            });
            html += `<div class="section-title">${this.t('settings.moviesTvShows')}</div><div style="display:flex;flex-wrap:wrap;gap:14px;margin-bottom:20px">`;
            unique.forEach(v => {
                const poster = v.posterPath ? `/videometa/${v.posterPath}` : '';
                const title = v.seriesName || v.title;
                const click = (v.mediaType === 'tv' || v.mediaType === 'anime') && v.seriesName
                    ? `App.openSeriesDetail('${title.replace(/'/g, "\\'")}')`
                    : `App.openVideoDetail(${v.id})`;
                html += `<div style="width:130px;cursor:pointer;text-align:center" onclick="${click}">
                    <div style="width:130px;height:185px;border-radius:8px;overflow:hidden;background:var(--bg-card);margin-bottom:6px;border:1px solid var(--border-color)">
                        ${poster
                            ? `<img src="${poster}" style="width:100%;height:100%;object-fit:cover" loading="lazy" alt="">`
                            : `<div style="display:flex;align-items:center;justify-content:center;height:100%"><svg style="width:40px;height:40px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></div>`}
                    </div>
                    <div style="font-size:12px;font-weight:500;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${this.esc(title)}</div>
                    <div style="font-size:11px;color:var(--text-secondary)">${v.mediaType === 'tv' ? 'TV' : v.mediaType === 'anime' ? 'Anime' : 'Movie'} ${v.year ? `(${v.year})` : ''}</div>
                </div>`;
            });
            html += '</div>';
        }

        if (totalResults === 0) {
            html += this.emptyState('No results', `Nothing found for "${this.esc(query)}".`);
        }
        el.innerHTML = html;
    },

    // ─── Toolbar ─────────────────────────────────────────────
    bindToolbar() {
        document.getElementById('btn-scan').addEventListener('click', () => this.showScanDialog());
        document.getElementById('btn-refresh').addEventListener('click', () => this.renderPage(this.currentPage));
    },

    // ─── Helpers ─────────────────────────────────────────────
    formatDuration(seconds) {
        if (!seconds || isNaN(seconds)) return '0:00';
        const s = Math.floor(seconds);
        if (s >= 3600) { const h = Math.floor(s/3600), m = Math.floor((s%3600)/60), sec = s%60; return `${h}:${m.toString().padStart(2,'0')}:${sec.toString().padStart(2,'0')}`; }
        return `${Math.floor(s/60)}:${(s%60).toString().padStart(2,'0')}`;
    },

    formatSize(bytes) {
        if (!bytes) return '0 B';
        const units = ['B','KB','MB','GB','TB'];
        let i = 0, size = bytes;
        while (size >= 1024 && i < units.length - 1) { size /= 1024; i++; }
        return `${size.toFixed(1)} ${units[i]}`;
    },

    esc(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    },

    // ── UI Template management ───────────────────────────────────
    async _loadTemplateGrid() {
        const grid = document.getElementById('tpl-grid');
        if (!grid) return;
        const templates = await this.api('templates').catch(() => []);
        if (!templates || !templates.length) return;
        const current = (document.getElementById('cfg-uiTemplate') || {}).value || '';
        for (const t of templates) {
            const preview = t.hasPreview
                ? `<img src="/templates/${this.esc(t.id)}/preview.png" style="width:100%;height:100%;object-fit:cover;border-radius:6px 6px 0 0">`
                : `<div class="tpl-preview-custom"><svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><use href="#icon-layout"/></svg><span>${this.esc(t.name)}</span></div>`;
            const card = document.createElement('div');
            card.className = `tpl-card${t.id === current ? ' tpl-card--active' : ''}`;
            card.setAttribute('data-tpl-id', t.id);
            card.onclick = () => this._selectTemplate(t.id);
            card.innerHTML = `<div class="tpl-card-preview">${preview}</div>
                <div class="tpl-card-body">
                    <div class="tpl-card-name">${this.esc(t.name)}</div>
                    <div class="tpl-card-desc">${this.esc(t.description)}</div>
                    <div class="tpl-card-meta"><span class="tpl-card-author">${this.esc(t.author)}</span><span class="tpl-card-version">v${this.esc(t.version)}</span></div>
                </div>`;
            grid.appendChild(card);
        }
    },

    _selectTemplate(id) {
        const input = document.getElementById('cfg-uiTemplate');
        if (input) input.value = id;
        document.querySelectorAll('.tpl-card').forEach(c => {
            c.classList.toggle('tpl-card--active', c.getAttribute('data-tpl-id') === id);
        });
    },

    emptyState(title, message) {
        return `<div class="empty-state"><div class="empty-icon">&#127925;</div><h2>${title}</h2><p>${message}</p></div>`;
    },

    // ── Video context menu & metadata editor ─────────────────────────

    _videoEditPosterData: null,
    _batchSelectMode: false,
    _batchSelectedIds: null,

    // ─── Batch Select / Edit ─────────────────────────────────────────────

    _batchToggleSelect() {
        this._batchSelectMode = !this._batchSelectMode;
        this._batchSelectedIds = this._batchSelectMode ? new Set() : null;
        this.loadVideosPage();
    },

    _batchToggleCard(id, cardEl) {
        if (!this._batchSelectedIds) return;
        const selected = this._batchSelectedIds.has(id);
        if (selected) {
            this._batchSelectedIds.delete(id);
            cardEl.classList.remove('batch-selected');
        } else {
            this._batchSelectedIds.add(id);
            cardEl.classList.add('batch-selected');
        }
        const cb = cardEl.querySelector('.batch-cb');
        if (cb) cb.checked = !selected;
        this._batchUpdateToolbar();
    },

    _batchSelectAll() {
        if (!this._batchSelectedIds) return;
        document.querySelectorAll('.mv-card[data-video-id]').forEach(card => {
            const id = parseInt(card.getAttribute('data-video-id'));
            if (!isNaN(id)) {
                this._batchSelectedIds.add(id);
                card.classList.add('batch-selected');
                const cb = card.querySelector('.batch-cb');
                if (cb) cb.checked = true;
            }
        });
        this._batchUpdateToolbar();
    },

    _batchClearSelection() {
        if (!this._batchSelectedIds) return;
        this._batchSelectedIds.clear();
        document.querySelectorAll('.mv-card.batch-selected').forEach(card => {
            card.classList.remove('batch-selected');
            const cb = card.querySelector('.batch-cb');
            if (cb) cb.checked = false;
        });
        this._batchUpdateToolbar();
    },

    _batchUpdateToolbar() {
        const count = this._batchSelectedIds?.size || 0;
        const countEl = document.getElementById('batch-toolbar-count');
        if (countEl) countEl.textContent = `${count} selected`;
        const editBtn = document.getElementById('batch-edit-btn');
        if (editBtn) editBtn.disabled = count === 0;
        // Keep filter-chip label in sync
        const batchChips = document.querySelectorAll('.batch-edit-chip');
        batchChips.forEach(chip => { chip.lastChild.textContent = `Batch (${count})`; });
    },

    openBatchEditModal() {
        if (!this._batchSelectedIds || this._batchSelectedIds.size === 0) return;
        let overlay = document.getElementById('batchEditOverlay');
        if (overlay) overlay.remove();
        overlay = document.createElement('div');
        overlay.id = 'batchEditOverlay';
        overlay.className = 'batch-edit-overlay';
        const n = this._batchSelectedIds.size;
        const svgPencil = `<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/></svg>`;
        overlay.innerHTML = `
        <div class="batch-edit-modal">
            <div class="batch-edit-title">${svgPencil} Batch Edit Metadata</div>
            <div class="batch-edit-subtitle">${n} item${n !== 1 ? 's' : ''} selected &mdash; tick each field you want to change. Unticked fields will not be modified.</div>

            <div class="batch-field">
                <input type="checkbox" class="batch-field-cb" id="bf-mediaType-cb" onchange="document.getElementById('bf-mediaType').disabled=!this.checked">
                <div class="batch-field-body">
                    <div class="batch-field-label">Media Type</div>
                    <select class="batch-field-select" id="bf-mediaType" disabled>
                        <option value="movie">Movie</option>
                        <option value="tv">TV Show</option>
                        <option value="documentary">Documentary</option>
                        <option value="anime">Anime</option>
                    </select>
                </div>
            </div>

            <div class="batch-field">
                <input type="checkbox" class="batch-field-cb" id="bf-genre-cb" onchange="document.getElementById('bf-genre').disabled=!this.checked">
                <div class="batch-field-body">
                    <div class="batch-field-label">Genre</div>
                    <input type="text" class="batch-field-input" id="bf-genre" disabled placeholder="e.g. Documentary, Nature, History">
                </div>
            </div>

            <div class="batch-field">
                <input type="checkbox" class="batch-field-cb" id="bf-year-cb" onchange="document.getElementById('bf-year').disabled=!this.checked">
                <div class="batch-field-body">
                    <div class="batch-field-label">Year</div>
                    <input type="number" class="batch-field-input" id="bf-year" disabled placeholder="e.g. 2023" min="1888" max="2099">
                </div>
            </div>

            <div class="batch-field">
                <input type="checkbox" class="batch-field-cb" id="bf-contentRating-cb" onchange="document.getElementById('bf-contentRating').disabled=!this.checked">
                <div class="batch-field-body">
                    <div class="batch-field-label">Content Rating</div>
                    <select class="batch-field-select" id="bf-contentRating" disabled>
                        <option value="">— select —</option>
                        <option value="G">G</option>
                        <option value="PG">PG</option>
                        <option value="PG-13">PG-13</option>
                        <option value="R">R</option>
                        <option value="NC-17">NC-17</option>
                        <option value="TV-Y">TV-Y</option>
                        <option value="TV-G">TV-G</option>
                        <option value="TV-PG">TV-PG</option>
                        <option value="TV-14">TV-14</option>
                        <option value="TV-MA">TV-MA</option>
                        <option value="NR">NR (Not Rated)</option>
                    </select>
                </div>
            </div>

            <div class="batch-field">
                <input type="checkbox" class="batch-field-cb" id="bf-safe-cb" onchange="document.getElementById('bf-safe-label').style.opacity=this.checked?'1':'.35';document.getElementById('bf-safe').disabled=!this.checked">
                <div class="batch-field-body">
                    <div class="batch-field-label">Safe for Children</div>
                    <label id="bf-safe-label" class="batch-field-check" style="opacity:.35">
                        <input type="checkbox" id="bf-safe" disabled style="width:15px;height:15px;accent-color:var(--accent)">
                        <span>Mark as safe for child users regardless of content rating</span>
                    </label>
                </div>
            </div>

            <div class="batch-edit-actions">
                <button onclick="App.closeBatchEditModal()" class="batch-edit-btn batch-edit-btn-cancel">Cancel</button>
                <button onclick="App.saveBatchEdit()" class="batch-edit-btn batch-edit-btn-primary" id="batch-save-btn">Apply to ${n} item${n !== 1 ? 's' : ''}</button>
            </div>
            <div class="batch-edit-note">
                <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="flex-shrink:0;margin-top:1px"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                Manually edited metadata will not be overwritten by future TMDB auto-fetch.
            </div>
        </div>`;
        document.body.appendChild(overlay);
        overlay.addEventListener('click', (e) => { if (e.target === overlay) this.closeBatchEditModal(); });
    },

    closeBatchEditModal() {
        const el = document.getElementById('batchEditOverlay');
        if (el) el.remove();
    },

    async saveBatchEdit() {
        const fields = {};
        if (document.getElementById('bf-mediaType-cb')?.checked) fields.mediaType = document.getElementById('bf-mediaType').value;
        if (document.getElementById('bf-genre-cb')?.checked) fields.genre = document.getElementById('bf-genre').value;
        if (document.getElementById('bf-year-cb')?.checked) { const y = parseInt(document.getElementById('bf-year').value); if (y) fields.year = y; }
        if (document.getElementById('bf-contentRating-cb')?.checked) fields.contentRating = document.getElementById('bf-contentRating').value;
        if (document.getElementById('bf-safe-cb')?.checked) fields.safeForChildren = document.getElementById('bf-safe')?.checked ?? false;

        if (Object.keys(fields).length === 0) { alert('Please tick at least one field to update.'); return; }

        const btn = document.getElementById('batch-save-btn');
        const origText = btn?.textContent;
        if (btn) { btn.disabled = true; btn.textContent = 'Saving…'; }

        try {
            const res = await fetch('/api/videos/batch', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ids: [...this._batchSelectedIds], fields })
            });
            const result = await res.json();
            if (result.success) {
                this.closeBatchEditModal();
                this._batchSelectedIds = new Set();
                this._batchSelectMode = false;
                this.loadVideosPage();
            } else {
                alert('Error: ' + (result.error || 'Unknown error'));
                if (btn) { btn.disabled = false; btn.textContent = origText; }
            }
        } catch (err) {
            alert('Error: ' + err.message);
            if (btn) { btn.disabled = false; btn.textContent = origText; }
        }
    },

    showVideoMenu(videoId, mediaType, event) {
        event.preventDefault();
        event.stopPropagation();
        this.closeVideoMenu();

        const menu = document.createElement('div');
        menu.id = 'videoContextMenu';
        menu.className = 'video-context-menu';

        const check = (type) => mediaType === type ? '<span class="video-menu-check">&#10003;</span>' : '';

        const svgAttr = 'xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"';
        menu.innerHTML = `
            <div class="video-menu-item" onclick="App.openVideoEditModal(${videoId})">
                <span class="video-menu-icon"><svg ${svgAttr}><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/></svg></span><span>${this.t('videomenu.editMetadata')}</span>
            </div>
            ${mediaType === 'anime'
                ? `<div class="video-menu-item" onclick="App.refetchAnimeMetadata(${videoId})">
                <span class="video-menu-icon"><svg ${svgAttr}><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg></span><span>${this.t('videomenu.refetchJikan')}</span>
            </div>`
                : `<div class="video-menu-item" onclick="App.refetchVideoMetadata(${videoId})">
                <span class="video-menu-icon"><svg ${svgAttr}><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg></span><span>${this.t('videomenu.refetchTmdb')}</span>
            </div>`
            }
            ${(mediaType === 'movie' || mediaType === 'tv') ? `
            <div class="video-menu-item" onclick="App.watchVideoTrailer(${videoId}, '${mediaType}', this)">
                <span class="video-menu-icon"><svg ${svgAttr}><polygon points="5 3 19 12 5 21 5 3"/></svg></span><span>${this.t('newreleases.trailer')}</span>
            </div>` : ''}
            <div class="video-menu-divider"></div>
            <div class="video-menu-item" onclick="App.setVideoMediaType(${videoId}, 'movie')">
                <span class="video-menu-icon"><svg ${svgAttr}><rect x="2" y="2" width="20" height="20" rx="2.18" ry="2.18"/><line x1="7" y1="2" x2="7" y2="22"/><line x1="17" y1="2" x2="17" y2="22"/><line x1="2" y1="12" x2="22" y2="12"/><line x1="2" y1="7" x2="7" y2="7"/><line x1="2" y1="17" x2="7" y2="17"/><line x1="17" y1="7" x2="22" y2="7"/><line x1="17" y1="17" x2="22" y2="17"/></svg></span><span>${this.t('videomenu.setAsMovie')}</span>
                ${check('movie')}
            </div>
            <div class="video-menu-item" onclick="App.setVideoMediaType(${videoId}, 'tv')">
                <span class="video-menu-icon"><svg ${svgAttr}><rect x="2" y="7" width="20" height="15" rx="2" ry="2"/><polyline points="17 2 12 7 7 2"/></svg></span><span>${this.t('videomenu.setAsTv')}</span>
                ${check('tv')}
            </div>
            <div class="video-menu-item" onclick="App.setVideoMediaType(${videoId}, 'documentary')">
                <span class="video-menu-icon"><svg ${svgAttr}><circle cx="12" cy="12" r="10"/><polygon points="10 8 16 12 10 16 10 8"/></svg></span><span>${this.t('videomenu.setAsDocumentary')}</span>
                ${check('documentary')}
            </div>
            <div class="video-menu-item" onclick="App.setVideoMediaType(${videoId}, 'anime')">
                <span class="video-menu-icon"><svg ${svgAttr}><path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"/></svg></span><span>${this.t('videomenu.setAsAnime')}</span>
                ${check('anime')}
            </div>
            <div class="video-menu-divider"></div>
            <div class="video-menu-item" onclick="App.toggleVideoWatched(${videoId})">
                <span class="video-menu-icon"><svg ${svgAttr}><polyline points="20 6 9 17 4 12"/></svg></span><span>${this._isVideoWatched(videoId) ? this.t('videomenu.markUnwatched') : this.t('videomenu.markWatched')}</span>
                ${this._isVideoWatched(videoId) ? '<span class="video-menu-check">&#10003;</span>' : ''}
            </div>
            <div class="video-menu-item" onclick="App.toggleVideoWatchlist(${videoId})">
                <span class="video-menu-icon"><svg ${svgAttr}><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"/></svg></span><span>${this._watchlistIds.has(videoId) ? this.t('watchlist.remove') : this.t('watchlist.add')}</span>
                ${this._watchlistIds.has(videoId) ? '<span class="video-menu-check">&#10003;</span>' : ''}
            </div>
            <div class="video-menu-divider"></div>
            <div class="video-menu-item" onclick="App.openRatingPopup('video', ${videoId}, event)">
                <span class="video-menu-icon">&#9733;</span><span>${this.t('videomenu.rate')}</span>
            </div>
            <div class="video-menu-divider"></div>
            <div class="video-menu-item video-menu-item-danger" onclick="App.deleteVideoFromDb(${videoId})">
                <span class="video-menu-icon"><svg ${svgAttr}><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/></svg></span><span>${this.t('videomenu.removeFromDb')}</span>
            </div>`;

        const rect = event.target.getBoundingClientRect();
        menu.style.position = 'fixed';
        menu.style.top = (rect.bottom + 4) + 'px';
        menu.style.left = (rect.left - 140) + 'px';

        document.body.appendChild(menu);

        // Keep within viewport
        const menuRect = menu.getBoundingClientRect();
        if (menuRect.right > window.innerWidth) menu.style.left = (window.innerWidth - menuRect.width - 8) + 'px';
        if (menuRect.left < 0) menu.style.left = '8px';
        if (menuRect.bottom > window.innerHeight) menu.style.top = (rect.top - menuRect.height - 4) + 'px';

        setTimeout(() => {
            const close = (e) => {
                if (!menu.contains(e.target)) { this.closeVideoMenu(); document.removeEventListener('click', close); }
            };
            document.addEventListener('click', close);
        }, 0);
    },

    closeVideoMenu() {
        const m = document.getElementById('videoContextMenu');
        if (m) m.remove();
    },

    async watchVideoTrailer(videoId, mediaType, el) {
        this.closeVideoMenu();
        const orig = el ? el.innerHTML : '';
        if (el) { el.textContent = 'Loading…'; el.style.pointerEvents = 'none'; }
        try {
            const video = await this.api(`videos/${videoId}`);
            const tmdbId = video && video.tmdbId;
            if (!tmdbId) {
                alert('No TMDB ID found for this title. Try re-fetching metadata first.');
                return;
            }
            const tmdbType = mediaType === 'tv' ? 'tv' : 'movie';
            const data = await this.api(`new-releases/trailer?type=${tmdbType}&id=${tmdbId}`);
            if (!data || !data.url) {
                alert('No trailer found for this title.');
                return;
            }
            window.open(data.url, '_blank', 'noopener,noreferrer');
        } catch (e) {
            alert('Error fetching trailer: ' + e.message);
        } finally {
            if (el) { el.innerHTML = orig; el.style.pointerEvents = ''; }
        }
    },

    showPlaylistMenu(id, event) {
        event.preventDefault();
        event.stopPropagation();
        this.closePlaylistMenu();
        this.closeVideoMenu();

        const p = (this._playlistsData || []).find(x => x.id === id) || { name: '', coverImagePath: '' };

        const svgAttr = 'xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"';
        const menu = document.createElement('div');
        menu.id = 'playlistContextMenu';
        menu.className = 'video-context-menu';
        menu.innerHTML = `
            <div class="video-menu-item" id="plMenuEdit">
                <span class="video-menu-icon"><svg ${svgAttr}><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/></svg></span><span>Edit Playlist</span>
            </div>
            <div class="video-menu-divider"></div>
            <div class="video-menu-item video-menu-item-danger" id="plMenuDelete">
                <span class="video-menu-icon"><svg ${svgAttr}><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/></svg></span><span>Delete Playlist</span>
            </div>`;

        const rect = event.target.getBoundingClientRect();
        menu.style.position = 'fixed';
        menu.style.top = (rect.bottom + 4) + 'px';
        menu.style.left = (rect.left - 140) + 'px';
        document.body.appendChild(menu);

        // Attach handlers after DOM insertion — avoids any inline string escaping issues
        menu.querySelector('#plMenuEdit').addEventListener('click', () => {
            this.closePlaylistMenu();
            this.editPlaylist(id, p.coverImagePath || '', p.name || '');
        });
        menu.querySelector('#plMenuDelete').addEventListener('click', () => {
            this.closePlaylistMenu();
            this.deletePlaylist(id);
        });

        const menuRect = menu.getBoundingClientRect();
        if (menuRect.right > window.innerWidth) menu.style.left = (window.innerWidth - menuRect.width - 8) + 'px';
        if (menuRect.left < 0) menu.style.left = '8px';
        if (menuRect.bottom > window.innerHeight) menu.style.top = (rect.top - menuRect.height - 4) + 'px';

        setTimeout(() => {
            const close = (e) => {
                if (!menu.contains(e.target)) { this.closePlaylistMenu(); document.removeEventListener('click', close); }
            };
            document.addEventListener('click', close);
        }, 0);
    },

    closePlaylistMenu() {
        const m = document.getElementById('playlistContextMenu');
        if (m) m.remove();
    },

    // ─── AGP 3-dot Menu ───────────────────────────────────────────────────

    showAgpMenu(type, param, event) {
        event.preventDefault();
        event.stopPropagation();
        this.closePlaylistMenu();
        this.closeVideoMenu();
        const existing = document.getElementById('agpContextMenu');
        if (existing) existing.remove();

        const svgAttr = 'xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"';
        const menu = document.createElement('div');
        menu.id = 'agpContextMenu';
        menu.className = 'video-context-menu';
        menu.innerHTML = `
            <div class="video-menu-item" id="agpMenuEditCover">
                <span class="video-menu-icon"><svg ${svgAttr}><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg></span><span>Edit Cover Image</span>
            </div>
            <div class="video-menu-divider"></div>
            <div class="video-menu-item video-menu-item-danger" id="agpMenuRemove">
                <span class="video-menu-icon"><svg ${svgAttr}><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/></svg></span><span>Remove Playlist</span>
            </div>`;

        const rect = event.target.getBoundingClientRect();
        menu.style.position = 'fixed';
        menu.style.top = (rect.bottom + 4) + 'px';
        menu.style.left = (rect.left - 140) + 'px';
        document.body.appendChild(menu);

        menu.querySelector('#agpMenuEditCover').addEventListener('click', () => {
            const m = document.getElementById('agpContextMenu');
            if (m) m.remove();
            this.openAgpEditCover(type, param);
        });
        menu.querySelector('#agpMenuRemove').addEventListener('click', () => {
            const m = document.getElementById('agpContextMenu');
            if (m) m.remove();
            this._agpDeletePlaylist(type, param);
        });

        const menuRect = menu.getBoundingClientRect();
        if (menuRect.right > window.innerWidth) menu.style.left = (window.innerWidth - menuRect.width - 8) + 'px';
        if (menuRect.left < 0) menu.style.left = '8px';
        if (menuRect.bottom > window.innerHeight) menu.style.top = (rect.top - menuRect.height - 4) + 'px';

        setTimeout(() => {
            const close = (e) => {
                const m = document.getElementById('agpContextMenu');
                if (m && !m.contains(e.target)) { m.remove(); document.removeEventListener('click', close); }
            };
            document.addEventListener('click', close);
        }, 0);
    },

    async openAgpEditCover(type, param) {
        const coverKey = type === 'decade' ? `decade_${param}` : type;
        const covers = this._agpConfig?.covers || this._agpConfig?.Covers || {};
        const currentCover = covers[coverKey] || '';

        this._agpEditCoverKey = coverKey;
        this._agpEditCoverValue = currentCover;

        const overlay = document.createElement('div');
        overlay.id = 'agpEditCoverOverlay';
        overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.65);z-index:3000;display:flex;align-items:center;justify-content:center';
        overlay.innerHTML = `
        <div style="background:var(--bg-surface);border-radius:12px;padding:28px;width:420px;max-width:95vw;box-shadow:0 8px 40px rgba(0,0,0,.5);position:relative">
            <button onclick="document.getElementById('agpEditCoverOverlay').remove()" style="position:absolute;top:12px;right:14px;background:none;border:none;color:var(--text-secondary);font-size:20px;cursor:pointer;line-height:1">&times;</button>
            <h2 style="margin:0 0 20px;font-size:17px;display:flex;align-items:center;gap:8px">
                <svg width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                Edit Cover Image
            </h2>
            <div style="display:flex;align-items:center;gap:12px;margin-bottom:24px">
                <div id="agpEditCoverPreview" style="width:72px;height:72px;border-radius:8px;overflow:hidden;background:var(--bg-hover);border:1px solid var(--border);flex-shrink:0;display:flex;align-items:center;justify-content:center">
                    ${currentCover
                        ? `<img src="/albumart/${this.esc(currentCover)}" style="width:100%;height:100%;object-fit:cover" alt="">`
                        : `<svg width="32" height="32" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" style="opacity:.4"><use href="#icon-music"/></svg>`}
                </div>
                <div style="display:flex;flex-direction:column;gap:7px">
                    <button onclick="App._agpPickCover()" style="background:var(--bg-hover);border:1px solid var(--border);border-radius:7px;padding:7px 14px;color:var(--text-primary);font-size:13px;cursor:pointer;display:flex;align-items:center;gap:7px">
                        <svg width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                        Pick from Album Art…</button>
                    ${currentCover ? `<button onclick="App._agpClearCover()" style="background:none;border:none;color:var(--text-secondary);font-size:12px;cursor:pointer;text-align:left;padding:0;display:flex;align-items:center;gap:5px"><svg width="12" height="12" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg> Remove cover</button>` : ''}
                </div>
            </div>
            <div style="display:flex;justify-content:flex-end;gap:10px">
                <button onclick="document.getElementById('agpEditCoverOverlay').remove()" style="background:var(--bg-hover);border:1px solid var(--border);border-radius:7px;padding:9px 18px;color:var(--text-primary);font-size:13px;cursor:pointer">Cancel</button>
                <button onclick="App._agpSaveCover()" style="background:var(--accent);border:none;border-radius:7px;padding:9px 18px;color:#fff;font-size:13px;font-weight:600;cursor:pointer">Save</button>
            </div>
        </div>`;
        document.body.appendChild(overlay);
        overlay.addEventListener('mousedown', (e) => { overlay._mdBackdrop = (e.target === overlay); });
        overlay.addEventListener('click', (e) => { if (e.target === overlay && overlay._mdBackdrop) overlay.remove(); });
    },

    _agpPickCover() {
        this._openMusicArtPicker('agp');
    },

    _agpClearCover() {
        this._agpEditCoverValue = '';
        const prev = document.getElementById('agpEditCoverPreview');
        if (prev) prev.innerHTML = `<svg width="32" height="32" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" style="opacity:.4"><use href="#icon-music"/></svg>`;
    },

    async _agpSaveCover() {
        if (!this._agpConfig) this._agpConfig = {};
        if (!this._agpConfig.covers) this._agpConfig.covers = {};
        if (this._agpEditCoverValue) {
            this._agpConfig.covers[this._agpEditCoverKey] = this._agpEditCoverValue;
        } else {
            delete this._agpConfig.covers[this._agpEditCoverKey];
        }
        const config = {
            activeTypes: this._agpConfig.activeTypes || this._agpConfig.ActiveTypes || [],
            excludedDecades: this._agpConfig.excludedDecades || this._agpConfig.ExcludedDecades || [],
            excludedTracks: this._agpConfig.excludedTracks || this._agpConfig.ExcludedTracks || {},
            covers: this._agpConfig.covers
        };
        await this.apiPost('agp-config', config);
        const overlay = document.getElementById('agpEditCoverOverlay');
        if (overlay) overlay.remove();
        this._agpRenderSections();
    },

    // ─── Album 3-dot Menu ─────────────────────────────────────────────────

    showAlbumMenu(albumId, event) {
        event.preventDefault();
        event.stopPropagation();
        this.closePlaylistMenu();
        this.closeVideoMenu();
        document.getElementById('albumContextMenu')?.remove();
        document.getElementById('trackContextMenu')?.remove();

        const svgAttr = 'xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"';
        const menu = document.createElement('div');
        menu.id = 'albumContextMenu';
        menu.className = 'video-context-menu';
        menu.innerHTML = `
            <div class="video-menu-item" id="albMenuEdit">
                <span class="video-menu-icon"><svg ${svgAttr}><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/></svg></span><span>Edit Album Metadata</span>
            </div>`;

        const rect = event.target.getBoundingClientRect();
        menu.style.position = 'fixed';
        menu.style.top = (rect.bottom + 4) + 'px';
        menu.style.left = (rect.left - 160) + 'px';
        document.body.appendChild(menu);

        menu.querySelector('#albMenuEdit').addEventListener('click', () => {
            menu.remove();
            this.openAlbumEditModal(albumId);
        });

        const menuRect = menu.getBoundingClientRect();
        if (menuRect.right > window.innerWidth) menu.style.left = (window.innerWidth - menuRect.width - 8) + 'px';
        if (menuRect.left < 0) menu.style.left = '8px';
        if (menuRect.bottom > window.innerHeight) menu.style.top = (rect.top - menuRect.height - 4) + 'px';

        setTimeout(() => {
            const close = (e) => { if (!menu.contains(e.target)) { menu.remove(); document.removeEventListener('click', close); } };
            document.addEventListener('click', close);
        }, 0);
    },

    // ─── Track 3-dot Menu ─────────────────────────────────────────────────

    showTrackMenu(trackId, event) {
        event.preventDefault();
        event.stopPropagation();
        this.closePlaylistMenu();
        this.closeVideoMenu();
        document.getElementById('albumContextMenu')?.remove();
        document.getElementById('trackContextMenu')?.remove();

        const svgAttr = 'xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"';
        const menu = document.createElement('div');
        menu.id = 'trackContextMenu';
        menu.className = 'video-context-menu';
        menu.innerHTML = `
            <div class="video-menu-item" id="trMenuEdit">
                <span class="video-menu-icon"><svg ${svgAttr}><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/></svg></span><span>Edit Track Metadata</span>
            </div>`;

        const rect = event.target.getBoundingClientRect();
        menu.style.position = 'fixed';
        menu.style.top = (rect.bottom + 4) + 'px';
        menu.style.left = (rect.left - 160) + 'px';
        document.body.appendChild(menu);

        menu.querySelector('#trMenuEdit').addEventListener('click', () => {
            menu.remove();
            this.openTrackEditModal(trackId);
        });

        const menuRect = menu.getBoundingClientRect();
        if (menuRect.right > window.innerWidth) menu.style.left = (window.innerWidth - menuRect.width - 8) + 'px';
        if (menuRect.left < 0) menu.style.left = '8px';
        if (menuRect.bottom > window.innerHeight) menu.style.top = (rect.top - menuRect.height - 4) + 'px';

        setTimeout(() => {
            const close = (e) => { if (!menu.contains(e.target)) { menu.remove(); document.removeEventListener('click', close); } };
            document.addEventListener('click', close);
        }, 0);
    },

    // ─── Album Edit Modal ─────────────────────────────────────────────────

    _albumEditCoverData: null,

    async openAlbumEditModal(albumId) {
        const album = await this.api(`albums/${albumId}`);
        if (!album) return;
        this._albumEditCoverData = null;
        this._albumEditPickedFile = null;

        const coverSrc = `/api/cover/${albumId}`;
        const overlay = document.createElement('div');
        overlay.id = 'albumEditOverlay';
        overlay.className = 'video-edit-overlay';
        overlay.innerHTML = `
        <div class="video-edit-modal">
            <button class="video-edit-close" onclick="App.closeAlbumEditModal()">&times;</button>
            <h2>
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:20px;height:20px">
                    <path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/>
                </svg>
                Edit Album Metadata
            </h2>
            <input type="hidden" id="albumEditId" value="${album.id}">

            <div class="video-edit-form-group">
                <label class="video-edit-label">Album Name</label>
                <input type="text" id="albumEditName" class="video-edit-input" value="${this.esc(album.name || '')}">
            </div>

            <div class="video-edit-row">
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Artist</label>
                    <input type="text" id="albumEditArtist" class="video-edit-input" value="${this.esc(album.artist || '')}">
                </div>
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Year</label>
                    <input type="number" id="albumEditYear" class="video-edit-input" value="${album.year || ''}">
                </div>
            </div>

            <div class="video-edit-form-group">
                <label class="video-edit-label">Genre</label>
                <input type="text" id="albumEditGenre" class="video-edit-input" value="${this.esc(album.genre || '')}" placeholder="Country, Rock, Pop…" list="albumEditGenreList" autocomplete="off">
                <datalist id="albumEditGenreList">${this._genericGenreOptions()}</datalist>
            </div>

            <div class="video-edit-form-group">
                <label class="video-edit-label">Cover Image</label>
                <div style="display:flex;align-items:flex-start;gap:14px;margin-bottom:10px">
                    <div id="albumEditCoverPreview" style="width:80px;height:80px;border-radius:8px;overflow:hidden;background:var(--bg-hover);border:1px solid var(--border);flex-shrink:0;display:flex;align-items:center;justify-content:center">
                        <img id="albumEditCoverImg" src="${coverSrc}" style="width:100%;height:100%;object-fit:cover" onerror="this.style.display='none'" alt="">
                    </div>
                    <div style="display:flex;flex-direction:column;gap:7px">
                        <button onclick="App._openMusicArtPicker('album')" style="background:var(--bg-hover);border:1px solid var(--border);border-radius:7px;padding:7px 14px;color:var(--text-primary);font-size:13px;cursor:pointer;display:flex;align-items:center;gap:7px">
                            <svg width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                            Pick from Library…</button>
                        <label style="background:var(--bg-hover);border:1px solid var(--border);border-radius:7px;padding:7px 14px;color:var(--text-primary);font-size:13px;cursor:pointer;display:flex;align-items:center;gap:7px">
                            <svg width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
                            Upload Image…
                            <input type="file" id="albumEditCoverFile" accept="image/jpeg,image/jpg,image/png" style="display:none" onchange="App.handleAlbumEditCover(event)">
                        </label>
                    </div>
                </div>
                <div id="albumEditCoverError" style="color:var(--danger);font-size:12px;display:none"></div>
            </div>

            <div class="video-edit-actions">
                <button onclick="App.closeAlbumEditModal()" class="video-edit-btn video-edit-btn-secondary">Cancel</button>
                <button onclick="App.saveAlbumMetadata()" class="video-edit-btn video-edit-btn-primary">Save Changes</button>
            </div>

            <div class="video-edit-note">
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="vertical-align:-2px;margin-right:4px;flex-shrink:0"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                Changes affect the album record only — individual track tags are not rewritten.
            </div>
        </div>`;

        document.body.appendChild(overlay);
        overlay.addEventListener('mousedown', (e) => { overlay._mdb = (e.target === overlay); });
        overlay.addEventListener('click', (e) => { if (e.target === overlay && overlay._mdb) this.closeAlbumEditModal(); });
        this._populateGenreDatalist('albumEditGenreList');
    },

    closeAlbumEditModal() {
        document.getElementById('albumEditOverlay')?.remove();
        this._albumEditCoverData = null;
        this._albumEditPickedFile = null;
    },

    handleAlbumEditCover(event) {
        const file = event.target.files[0];
        if (!file) return;
        const errDiv = document.getElementById('albumEditCoverError');
        if (!file.type.match(/image\/(jpeg|jpg|png)/i)) {
            if (errDiv) { errDiv.textContent = 'JPG or PNG only.'; errDiv.style.display = 'block'; }
            return;
        }
        if (errDiv) errDiv.style.display = 'none';
        const reader = new FileReader();
        reader.onload = (e) => {
            this._albumEditCoverData = e.target.result;
            this._albumEditPickedFile = null;
            const img = document.getElementById('albumEditCoverImg');
            if (img) { img.src = e.target.result; img.style.display = ''; }
        };
        reader.readAsDataURL(file);
    },

    async saveAlbumMetadata() {
        const id = document.getElementById('albumEditId').value;
        const payload = {
            name: document.getElementById('albumEditName').value,
            artist: document.getElementById('albumEditArtist').value,
            genre: document.getElementById('albumEditGenre').value,
            year: parseInt(document.getElementById('albumEditYear').value) || null,
            coverImage: this._albumEditCoverData || null,
            coverArtPath: this._albumEditPickedFile || null
        };
        try {
            const res = await fetch(`/api/albums/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            const result = await res.json();
            if (result.success) {
                this.closeAlbumEditModal();
                this.loadAlbumsPage();
            } else {
                alert('Error saving album: ' + (result.error || 'Unknown error'));
            }
        } catch (err) {
            alert('Error: ' + err.message);
        }
    },

    // ─── Track Edit Modal ─────────────────────────────────────────────────

    _trackEditCoverData: null,
    _trackEditPickedFile: null,

    async openTrackEditModal(trackId) {
        const [t, cgAssignments] = await Promise.all([
            this.api(`tracks/${trackId}`),
            this.api(`tracks/${trackId}/custom-genres`)
        ]);
        if (!t) return;
        this._trackEditCoverData = null;
        this._trackEditPickedFile = null;

        const artSrc = t.albumArtCached ? `/albumart/${t.albumArtCached}` : (t.hasAlbumArt ? `/api/cover/track/${t.id}` : null);
        const overlay = document.createElement('div');
        overlay.id = 'trackEditOverlay';
        overlay.className = 'video-edit-overlay';
        overlay.innerHTML = `
        <div class="video-edit-modal">
            <button class="video-edit-close" onclick="App.closeTrackEditModal()">&times;</button>
            <h2>
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:20px;height:20px">
                    <path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/>
                </svg>
                Edit Track Metadata
            </h2>
            <input type="hidden" id="trackEditId" value="${t.id}">

            <div class="video-edit-form-group">
                <label class="video-edit-label">Title</label>
                <input type="text" id="trackEditTitle" class="video-edit-input" value="${this.esc(t.title || '')}">
            </div>

            <div class="video-edit-row">
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Artist</label>
                    <input type="text" id="trackEditArtist" class="video-edit-input" value="${this.esc(t.artist || '')}">
                </div>
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Album Artist</label>
                    <input type="text" id="trackEditAlbumArtist" class="video-edit-input" value="${this.esc(t.albumArtist || '')}">
                </div>
            </div>

            <div class="video-edit-form-group">
                <label class="video-edit-label">Album</label>
                <input type="text" id="trackEditAlbum" class="video-edit-input" value="${this.esc(t.album || '')}">
            </div>

            <div class="video-edit-row">
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Genre</label>
                    <input type="text" id="trackEditGenre" class="video-edit-input" value="${this.esc(t.genre || '')}" placeholder="Country, Rock, Pop…" list="trackEditGenreList" autocomplete="off">
                    <datalist id="trackEditGenreList">${this._genericGenreOptions()}</datalist>
                </div>
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Year</label>
                    <input type="number" id="trackEditYear" class="video-edit-input" value="${t.year || ''}">
                </div>
            </div>

            <div class="video-edit-row">
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Track #</label>
                    <input type="number" id="trackEditTrackNum" class="video-edit-input" value="${t.trackNumber || ''}">
                </div>
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Composer</label>
                    <input type="text" id="trackEditComposer" class="video-edit-input" value="${this.esc(t.composer || '')}">
                </div>
            </div>

            <div class="video-edit-form-group" style="padding:6px 0">
                <label style="display:flex;align-items:center;gap:10px;cursor:pointer;font-size:13px">
                    <input type="checkbox" id="trackEditApplyGenre" checked style="width:16px;height:16px;accent-color:var(--accent);flex-shrink:0">
                    <span>Apply this genre to <strong>all songs by this artist</strong> in the library</span>
                </label>
                <div style="margin-top:3px;font-size:11px;color:var(--text-muted);margin-left:26px">Updates every track where Artist = "${this.esc(t.artist || '')}"</div>
            </div>

            <div class="video-edit-form-group">
                <label class="video-edit-label">Cover Image</label>
                <div style="display:flex;align-items:flex-start;gap:14px;margin-bottom:10px">
                    <div id="trackEditCoverPreview" style="width:80px;height:80px;border-radius:8px;overflow:hidden;background:var(--bg-hover);border:1px solid var(--border);flex-shrink:0;display:flex;align-items:center;justify-content:center">
                        ${artSrc ? `<img id="trackEditCoverImg" src="${artSrc}" style="width:100%;height:100%;object-fit:cover" alt="">` : `<svg id="trackEditCoverImg" width="32" height="32" fill="none" stroke="currentColor" stroke-width="1.5" style="opacity:.4"><use href="#icon-music"/></svg>`}
                    </div>
                    <div style="display:flex;flex-direction:column;gap:7px">
                        <button onclick="App._openMusicArtPicker('track')" style="background:var(--bg-hover);border:1px solid var(--border);border-radius:7px;padding:7px 14px;color:var(--text-primary);font-size:13px;cursor:pointer;display:flex;align-items:center;gap:7px">
                            <svg width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                            Pick from Library…</button>
                        <label style="background:var(--bg-hover);border:1px solid var(--border);border-radius:7px;padding:7px 14px;color:var(--text-primary);font-size:13px;cursor:pointer;display:flex;align-items:center;gap:7px">
                            <svg width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
                            Upload Image…
                            <input type="file" id="trackEditCoverFile" accept="image/jpeg,image/jpg,image/png" style="display:none" onchange="App.handleTrackEditCover(event)">
                        </label>
                    </div>
                </div>
                <div id="trackEditCoverError" style="color:var(--danger);font-size:12px;display:none"></div>
            </div>

            ${cgAssignments && cgAssignments.length > 0 ? `
            <div class="video-edit-form-group">
                <label class="video-edit-label">${this.t('customGenres.title')}</label>
                <div style="font-size:11px;color:var(--text-secondary);margin-bottom:6px">${this.t('customGenres.assignHint')}</div>
                <div class="cg-assign-list">
                    ${cgAssignments.map(cg => `
                    <label class="cg-assign-item">
                        <input type="checkbox" class="cg-assign-check" data-id="${cg.id}"${cg.assigned ? ' checked' : ''}>
                        <span>${this.esc(cg.name)}</span>
                    </label>`).join('')}
                </div>
            </div>` : ''}

            <div class="video-edit-actions">
                <button onclick="App.closeTrackEditModal()" class="video-edit-btn video-edit-btn-secondary">Cancel</button>
                <button onclick="App.saveTrackMetadata()" class="video-edit-btn video-edit-btn-primary">Save Changes</button>
            </div>

            <div class="video-edit-note">
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="vertical-align:-2px;margin-right:4px;flex-shrink:0"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                Changes update the library database only — audio file tags are not rewritten.
            </div>
        </div>`;

        document.body.appendChild(overlay);
        overlay.addEventListener('mousedown', (e) => { overlay._mdb = (e.target === overlay); });
        overlay.addEventListener('click', (e) => { if (e.target === overlay && overlay._mdb) this.closeTrackEditModal(); });
        this._populateGenreDatalist('trackEditGenreList');
    },

    closeTrackEditModal() {
        document.getElementById('trackEditOverlay')?.remove();
        this._trackEditCoverData = null;
        this._trackEditPickedFile = null;
    },

    handleTrackEditCover(event) {
        const file = event.target.files[0];
        if (!file) return;
        const errDiv = document.getElementById('trackEditCoverError');
        if (!file.type.match(/image\/(jpeg|jpg|png)/i)) {
            if (errDiv) { errDiv.textContent = 'JPG or PNG only.'; errDiv.style.display = 'block'; }
            return;
        }
        if (errDiv) errDiv.style.display = 'none';
        const reader = new FileReader();
        reader.onload = (e) => {
            this._trackEditCoverData = e.target.result;
            this._trackEditPickedFile = null;
            const prev = document.getElementById('trackEditCoverPreview');
            if (prev) prev.innerHTML = `<img src="${e.target.result}" style="width:100%;height:100%;object-fit:cover" alt="">`;
        };
        reader.readAsDataURL(file);
    },

    async saveTrackMetadata() {
        const id = document.getElementById('trackEditId').value;
        const applyGenre = document.getElementById('trackEditApplyGenre')?.checked ?? false;
        const payload = {
            title: document.getElementById('trackEditTitle').value,
            artist: document.getElementById('trackEditArtist').value,
            albumArtist: document.getElementById('trackEditAlbumArtist').value,
            album: document.getElementById('trackEditAlbum').value,
            genre: document.getElementById('trackEditGenre').value,
            year: parseInt(document.getElementById('trackEditYear').value) || null,
            trackNumber: parseInt(document.getElementById('trackEditTrackNum').value) || null,
            composer: document.getElementById('trackEditComposer').value,
            coverImage: this._trackEditCoverData || null,
            coverArtPath: this._trackEditPickedFile || null,
            applyGenreToArtist: applyGenre
        };
        try {
            const res = await fetch(`/api/tracks/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            const result = await res.json();
            if (result.success) {
                const cgChecks = document.querySelectorAll('.cg-assign-check');
                if (cgChecks.length > 0) {
                    const genreIds = Array.from(cgChecks).filter(c => c.checked).map(c => c.dataset.id);
                    await this.apiPost(`tracks/${id}/custom-genres`, { genreIds });
                }
                this.closeTrackEditModal();
                if (document.getElementById('music-sub-content')) this.loadSongsPage();
            } else {
                alert('Error saving track: ' + (result.error || 'Unknown error'));
            }
        } catch (err) {
            alert('Error: ' + err.message);
        }
    },

    // ─── Genre Datalist Helpers ───────────────────────────────────────────

    _genericGenreOptions() {
        const genres = ['Alternative','Ambient','Blues','Children\'s','Classical','Comedy','Country',
            'Dance','Disco','Electronic','Folk','Funk','Gospel','Hip-Hop','House','Indie','Jazz',
            'Latin','Metal','New Age','Opera','Pop','Punk','R&B','Reggae','Rock','Soul',
            'Soundtrack','Techno','World Music'];
        return genres.map(g => `<option value="${g}">`).join('');
    },

    async _populateGenreDatalist(datalistId) {
        const dl = document.getElementById(datalistId);
        if (!dl) return;
        const genres = await this.api('genres').catch(() => null);
        if (!genres || genres.length === 0) return;
        // Prepend library genres, deduplicating against existing generic options
        const existing = new Set([...dl.querySelectorAll('option')].map(o => o.value.toLowerCase()));
        const fragment = document.createDocumentFragment();
        genres.forEach(g => {
            if (!existing.has(g.name.toLowerCase())) {
                const opt = document.createElement('option');
                opt.value = g.name;
                fragment.appendChild(opt);
            }
        });
        dl.insertBefore(fragment, dl.firstChild);
    },

    // ─── Generic Music Art Picker (album art library) ─────────────────────

    async _openMusicArtPicker(target) {
        // target: 'album' | 'track' | 'playlist' | 'agp'
        const items = await this.api('albumart/list');
        if (!items || items.length === 0) { alert('No album art found in the library yet.'); return; }

        const picker = document.createElement('div');
        picker.id = 'musicArtPickerOverlay';
        picker.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.75);z-index:11000;display:flex;align-items:center;justify-content:center';
        picker.innerHTML = `
        <div style="background:var(--bg-surface);border-radius:12px;padding:20px;width:680px;max-width:96vw;max-height:80vh;display:flex;flex-direction:column;box-shadow:0 8px 40px rgba(0,0,0,.6)">
            <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:14px">
                <span style="font-size:15px;font-weight:600">Pick Cover Image</span>
                <button id="musicPickerClose" style="background:none;border:none;color:var(--text-secondary);font-size:20px;cursor:pointer;line-height:1">&times;</button>
            </div>
            <input id="musicArtSearch" type="text" placeholder="Search by artist or album…" style="width:100%;box-sizing:border-box;background:var(--bg-hover);border:1px solid var(--border);border-radius:7px;padding:7px 12px;color:var(--text-primary);font-size:13px;margin-bottom:12px">
            <div id="musicArtGrid" style="display:grid;grid-template-columns:repeat(auto-fill,minmax(90px,1fr));gap:8px;overflow-y:auto;max-height:52vh;padding-right:4px"></div>
        </div>`;
        document.body.appendChild(picker);

        picker.querySelector('#musicPickerClose').addEventListener('click', () => picker.remove());
        picker.addEventListener('mousedown', (e) => { picker._mdb = (e.target === picker); });
        picker.addEventListener('click', (e) => { if (e.target === picker && picker._mdb) picker.remove(); });

        const grid = picker.querySelector('#musicArtGrid');
        const renderGrid = (list) => {
            grid.innerHTML = '';
            list.forEach(item => {
                const tile = document.createElement('div');
                tile.style.cssText = 'cursor:pointer;border-radius:8px;overflow:hidden;aspect-ratio:1;background:var(--bg-hover);border:2px solid transparent;transition:border-color .15s;position:relative';
                tile.title = [item.artist, item.album].filter(Boolean).join(' — ') || item.filename;
                tile.innerHTML = `<img src="/albumart/${this.esc(item.filename)}" style="width:100%;height:100%;object-fit:cover" loading="lazy" alt="">`;
                tile.addEventListener('mouseenter', () => { tile.style.borderColor = 'var(--accent)'; });
                tile.addEventListener('mouseleave', () => { tile.style.borderColor = 'transparent'; });
                tile.addEventListener('click', () => {
                    picker.remove();
                    if (target === 'playlist') {
                        this._plSelectCover(item.filename);
                    } else if (target === 'album') {
                        this._albumEditPickedFile = item.filename;
                        this._albumEditCoverData = null;
                        const img = document.getElementById('albumEditCoverImg');
                        if (img) { img.src = `/albumart/${item.filename}`; img.style.display = ''; }
                    } else if (target === 'track') {
                        this._trackEditPickedFile = item.filename;
                        this._trackEditCoverData = null;
                        const prev = document.getElementById('trackEditCoverPreview');
                        if (prev) prev.innerHTML = `<img src="/albumart/${this.esc(item.filename)}" style="width:100%;height:100%;object-fit:cover" alt="">`;
                    } else if (target === 'agp') {
                        this._agpEditCoverValue = item.filename;
                        const prev = document.getElementById('agpEditCoverPreview');
                        if (prev) prev.innerHTML = `<img src="/albumart/${this.esc(item.filename)}" style="width:100%;height:100%;object-fit:cover" alt="">`;
                    }
                });
                grid.appendChild(tile);
            });
        };
        renderGrid(items);

        picker.querySelector('#musicArtSearch').addEventListener('input', (e) => {
            const q = e.target.value.toLowerCase().trim();
            const filtered = q ? items.filter(item =>
                (item.artist || '').toLowerCase().includes(q) ||
                (item.album  || '').toLowerCase().includes(q) ||
                (item.filename || '').toLowerCase().includes(q)
            ) : items;
            renderGrid(filtered);
        });
    },

    async setVideoMediaType(videoId, newType) {
        this.closeVideoMenu();
        const res = await fetch(`/api/videos/${videoId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ mediaType: newType })
        });
        if (!res.ok) return;
        // When marking as anime, kick off Jikan enrichment and navigate to anime page
        if (newType === 'anime') {
            fetch(`/api/videos/${videoId}/fetch-anime`, { method: 'POST' }).catch(() => {});
            this.navigate('anime');
        } else {
            this.loadVideosPage();
        }
    },

    async deleteVideoFromDb(videoId) {
        this.closeVideoMenu();
        if (!confirm('Remove this video from the NexusM database?\n\nThe video FILE will NOT be deleted from your disk — only the library entry will be removed.')) return;
        if (!confirm('Second confirmation required — this cannot be undone.\n\nPress OK to permanently remove this entry from the database.')) return;
        const res = await this.apiDelete(`videos/${videoId}`);
        if (res && res.success) {
            this.loadVideosPage();
        } else {
            alert('Failed to remove the video from the database.');
        }
    },

    _isVideoWatched(videoId) {
        const card = document.querySelector(`[data-video-id="${videoId}"]`);
        return card ? card.getAttribute('data-watched') === '1' : false;
    },

    async toggleVideoWatched(videoId) {
        this.closeVideoMenu();
        try {
            const res = await fetch(`/api/videos/${videoId}/watched`, { method: 'POST' });
            if (res.ok) {
                const data = await res.json();
                // Update the card's data attribute and badge
                const card = document.querySelector(`[data-video-id="${videoId}"]`);
                if (card) {
                    card.setAttribute('data-watched', data.watched ? '1' : '0');
                    const thumb = card.querySelector('.mv-card-thumb');
                    // Remove existing badge
                    const existing = thumb.querySelector('.mv-watched-badge');
                    if (existing) existing.remove();
                    // Add badge if now watched
                    if (data.watched) {
                        const badge = document.createElement('span');
                        badge.className = 'mv-watched-badge';
                        badge.innerHTML = '<svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>WATCHED';
                        thumb.insertBefore(badge, thumb.firstChild);
                    }
                }
            }
        } catch (e) { console.error('Failed to toggle watched:', e); }
    },

    async refetchVideoMetadata(videoId) {
        this.closeVideoMenu();
        await this.apiPost(`videos/${videoId}/refresh-metadata`);
        this.loadVideosPage();
    },

    async refetchAnimeMetadata(videoId) {
        this.closeVideoMenu();
        await fetch(`/api/videos/${videoId}/fetch-anime`, { method: 'POST' });
        // Endpoint is synchronous — Jikan is done, reload anime page to show updated data
        this.navigate('anime');
    },

    async openVideoEditModal(videoId) {
        this.closeVideoMenu();
        const [v, cgAssignments] = await Promise.all([
            this.api(`videos/${videoId}`),
            this.api(`videos/${videoId}/custom-genres`)
        ]);
        if (!v) return;

        this._videoEditPosterData = null;
        this._videoEditId = videoId;

        const overlay = document.createElement('div');
        overlay.id = 'videoEditOverlay';
        overlay.className = 'video-edit-overlay';
        overlay.innerHTML = `
        <div class="video-edit-modal">
            <button class="video-edit-close" onclick="App.closeVideoEditModal()">&times;</button>
            <h2>
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:20px;height:20px">
                    <path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/>
                </svg>
                Edit Video Metadata
            </h2>
            <input type="hidden" id="videoEditId" value="${v.id}">

            <div class="video-edit-form-group">
                <label class="video-edit-label">Title</label>
                <input type="text" id="videoEditTitle" class="video-edit-input" value="${this.esc(v.title || '')}">
            </div>

            <div class="video-edit-row">
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Year</label>
                    <input type="number" id="videoEditYear" class="video-edit-input" value="${v.year || ''}">
                </div>
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Media Type</label>
                    <select id="videoEditMediaType" class="video-edit-select">
                        <option value="movie" ${v.mediaType === 'movie' ? 'selected' : ''}>Movie</option>
                        <option value="tv" ${v.mediaType === 'tv' ? 'selected' : ''}>TV Show</option>
                        <option value="documentary" ${v.mediaType === 'documentary' ? 'selected' : ''}>Documentary</option>
                        <option value="anime" ${v.mediaType === 'anime' ? 'selected' : ''}>Anime</option>
                    </select>
                </div>
            </div>

            <div class="video-edit-row">
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Series Name</label>
                    <input type="text" id="videoEditSeriesName" class="video-edit-input" value="${this.esc(v.seriesName || '')}">
                </div>
                <div class="video-edit-row">
                    <div class="video-edit-form-group">
                        <label class="video-edit-label">Season</label>
                        <input type="number" id="videoEditSeason" class="video-edit-input" value="${v.season || ''}">
                    </div>
                    <div class="video-edit-form-group">
                        <label class="video-edit-label">Episode</label>
                        <input type="number" id="videoEditEpisode" class="video-edit-input" value="${v.episode || ''}">
                    </div>
                </div>
            </div>

            <div class="video-edit-form-group">
                <label class="video-edit-label">Genre</label>
                <input type="text" id="videoEditGenre" class="video-edit-input" value="${this.esc(v.genre || '')}" placeholder="Action, Drama, Sci-Fi">
            </div>

            <div class="video-edit-form-group">
                <label class="video-edit-label">Director</label>
                <input type="text" id="videoEditDirector" class="video-edit-input" value="${this.esc(v.director || '')}">
            </div>

            <div class="video-edit-form-group">
                <label class="video-edit-label">Cast / Actors</label>
                <input type="text" id="videoEditCast" class="video-edit-input" value="${this.esc(v.cast || '')}" placeholder="Comma-separated names">
            </div>

            <div class="video-edit-row">
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Rating (0-10)</label>
                    <input type="number" step="0.1" min="0" max="10" id="videoEditRating" class="video-edit-input" value="${v.rating || ''}">
                </div>
                <div class="video-edit-form-group">
                    <label class="video-edit-label">Content Rating</label>
                    <input type="text" id="videoEditContentRating" class="video-edit-input" value="${this.esc(v.contentRating || '')}" placeholder="PG-13, R, TV-MA">
                </div>
            </div>

            <div class="video-edit-form-group" style="padding:6px 0">
                <label style="display:flex;align-items:center;gap:10px;cursor:pointer;font-size:13px">
                    <input type="checkbox" id="videoEditSafeForChildren"
                           ${v.safeForChildren ? 'checked' : ''}
                           style="width:16px;height:16px;accent-color:#22c55e;flex-shrink:0">
                    <span>${this.t('videoEdit.safeForChildren')}</span>
                </label>
                <div style="margin-top:3px;font-size:11px;color:var(--text-muted);margin-left:26px">${this.t('videoEdit.safeForChildrenHint')}</div>
            </div>

            ${cgAssignments && cgAssignments.length > 0 ? `
            <div class="video-edit-form-group">
                <label class="video-edit-label">${this.t('customGenres.title')}</label>
                <div class="cg-assign-list">
                    ${cgAssignments.map(cg => `<label class="cg-assign-item">
                        <input type="checkbox" class="cg-assign-check" value="${this.esc(cg.id)}" ${cg.assigned ? 'checked' : ''}>
                        <span>${this.esc(cg.name)}</span>
                    </label>`).join('')}
                </div>
                <div style="margin-top:5px;font-size:11px;color:var(--text-muted)">${this.t('customGenres.assignHint')}</div>
            </div>` : ''}

            <div class="video-edit-form-group">
                <label class="video-edit-label">Overview / Description</label>
                <textarea id="videoEditOverview" class="video-edit-input" rows="4" style="resize:vertical;font-family:inherit">${this.esc(v.overview || '')}</textarea>
            </div>

            <div class="video-edit-form-group">
                <label class="video-edit-label">Poster Image</label>
                <input type="file" id="videoEditPosterFile" class="video-edit-input" accept="image/jpeg,image/jpg,image/png" onchange="App.handleVideoEditPoster(event)">
                <div style="margin-top:4px;font-size:11px;color:var(--text-muted)">Min 300x450px &bull; Resized to 500x750px &bull; JPG/PNG only</div>
                ${v.posterPath ? `<div style="margin-top:8px"><img src="/videometa/${v.posterPath}" style="max-width:100px;border-radius:6px;opacity:.7"> <span style="font-size:11px;color:var(--text-muted)">Current poster</span></div>` : ''}
                <div id="videoEditPosterPreview" class="video-edit-poster-preview">
                    <img id="videoEditPosterPreviewImg">
                    <div id="videoEditPosterDims" class="video-edit-poster-dims"></div>
                </div>
                <div id="videoEditPosterError" class="video-edit-poster-error"></div>
            </div>

            <div class="video-edit-actions">
                <button onclick="App.closeVideoEditModal()" class="video-edit-btn video-edit-btn-secondary">Cancel</button>
                <button onclick="App.saveVideoMetadata()" class="video-edit-btn video-edit-btn-primary">Save Changes</button>
            </div>

            <div class="video-edit-note">
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="vertical-align:-2px;margin-right:4px;flex-shrink:0"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                Manually edited metadata will not be overwritten by TMDB auto-fetch.
            </div>
        </div>`;

        document.body.appendChild(overlay);
        overlay.addEventListener('mousedown', (e) => { overlay._mousedownOnBackdrop = (e.target === overlay); });
        overlay.addEventListener('click', (e) => { if (e.target === overlay && overlay._mousedownOnBackdrop) this.closeVideoEditModal(); });
    },

    closeVideoEditModal() {
        const el = document.getElementById('videoEditOverlay');
        if (el) el.remove();
        this._videoEditPosterData = null;
    },

    handleVideoEditPoster(event) {
        const file = event.target.files[0];
        const errorDiv = document.getElementById('videoEditPosterError');
        const previewDiv = document.getElementById('videoEditPosterPreview');
        const previewImg = document.getElementById('videoEditPosterPreviewImg');
        const dimsDiv = document.getElementById('videoEditPosterDims');

        if (errorDiv) errorDiv.style.display = 'none';
        if (previewDiv) previewDiv.style.display = 'none';
        this._videoEditPosterData = null;

        if (!file) return;

        if (!file.type.match(/image\/(jpeg|jpg|png)/)) {
            if (errorDiv) { errorDiv.textContent = 'Invalid file type. Please upload a JPG or PNG image.'; errorDiv.style.display = 'block'; }
            event.target.value = '';
            return;
        }

        const reader = new FileReader();
        reader.onload = (e) => {
            const img = new Image();
            img.onload = () => {
                if (img.width < 300 || img.height < 450) {
                    if (errorDiv) { errorDiv.textContent = `Image too small! Min 300x450px. Yours: ${img.width}x${img.height}px.`; errorDiv.style.display = 'block'; }
                    event.target.value = '';
                    return;
                }
                const canvas = document.createElement('canvas');
                canvas.width = 500; canvas.height = 750;
                const ctx = canvas.getContext('2d');
                ctx.drawImage(img, 0, 0, 500, 750);
                this._videoEditPosterData = canvas.toDataURL('image/jpeg', 0.9);
                if (previewImg) previewImg.src = this._videoEditPosterData;
                if (dimsDiv) dimsDiv.textContent = `Original: ${img.width}x${img.height}px → Resized to 500x750px`;
                if (previewDiv) previewDiv.style.display = 'block';
            };
            img.src = e.target.result;
        };
        reader.readAsDataURL(file);
    },

    // ─── Rating System ────────────────────────────────────────────

    // Build interactive 5-star picker HTML
    buildStarPicker(currentStars, mediaType, mediaId) {
        const stars = Array.from({ length: 5 }, (_, i) => {
            const val = i + 1;
            const filled = val <= currentStars ? ' filled' : '';
            return `<span class="star-btn${filled}" data-val="${val}"
                onmouseenter="App.hoverStars(this, ${val})"
                onmouseleave="App.resetStarHover(this)"
                onclick="App.rateMedia('${mediaType}', ${mediaId}, ${val})">&#9733;</span>`;
        }).join('');
        return `<div class="star-picker">${stars}</div>`;
    },

    hoverStars(starEl, value) {
        const picker = starEl.closest('.star-picker');
        picker.querySelectorAll('.star-btn').forEach((s, i) =>
            s.classList.toggle('hovered', i < value));
    },

    resetStarHover(starEl) {
        starEl.closest('.star-picker').querySelectorAll('.star-btn').forEach(s =>
            s.classList.remove('hovered'));
    },

    async rateMedia(mediaType, mediaId, stars) {
        await this.apiPost('ratings', { mediaType, mediaId, stars });
        document.querySelectorAll('.rating-popup').forEach(p => p.remove());
        await this._refreshRatingWidget(mediaType, mediaId);
    },

    async removeRating(mediaType, mediaId) {
        await this.apiDelete(`ratings/${mediaType}/${mediaId}`);
        document.querySelectorAll('.rating-popup').forEach(p => p.remove());
        await this._refreshRatingWidget(mediaType, mediaId);
    },

    async _refreshRatingWidget(mediaType, mediaId) {
        const summary = await this.api(`ratings/summary/${mediaType}/${mediaId}`);
        const widget = document.querySelector(`.rating-widget[data-mt="${mediaType}"][data-mid="${mediaId}"]`);
        if (widget && summary) widget.innerHTML = this._buildRatingWidgetInner(summary, mediaType, mediaId);
    },

    async openRatingPopup(mediaType, mediaId, event) {
        // Capture the context menu rect BEFORE removing it from the DOM.
        // After closeVideoMenu() the element is gone and getBoundingClientRect() returns all zeros.
        const contextMenu = document.getElementById('videoContextMenu');
        const menuRect = contextMenu ? contextMenu.getBoundingClientRect() : null;

        this.closeVideoMenu();
        document.querySelectorAll('.rating-popup').forEach(p => p.remove());
        const summary = await this.api(`ratings/summary/${mediaType}/${mediaId}`);

        const popup = document.createElement('div');
        popup.className = 'rating-popup';
        popup.innerHTML = `
            <div class="rating-popup-title">Your Rating</div>
            ${this.buildStarPicker(summary?.userRating || 0, mediaType, mediaId)}
            ${summary?.count > 0
                ? `<div class="rating-popup-avg">Community: ${summary.average.toFixed(1)}&#9733; from ${summary.count} rating${summary.count !== 1 ? 's' : ''}</div>` : ''}
            ${summary?.userRating > 0
                ? `<div class="rating-popup-remove" onclick="App.removeRating('${mediaType}', ${mediaId})">Remove my rating</div>` : ''}`;

        // Position where the context menu was so the popup appears next to the 3-dot button.
        // Fall back to the raw click coordinates if the menu rect is unavailable.
        const top = menuRect ? menuRect.top : event.clientY;
        const left = menuRect ? menuRect.left : (event.clientX - 120);
        popup.style.cssText = `position:fixed;top:${top}px;left:${left}px`;
        document.body.appendChild(popup);

        // Clamp to viewport
        const pr = popup.getBoundingClientRect();
        if (pr.right > window.innerWidth) popup.style.left = (window.innerWidth - pr.width - 8) + 'px';
        if (pr.left < 0) popup.style.left = '8px';
        if (pr.bottom > window.innerHeight) popup.style.top = (window.innerHeight - pr.height - 8) + 'px';

        setTimeout(() => {
            const close = (e) => {
                if (!popup.contains(e.target)) { popup.remove(); document.removeEventListener('click', close); }
            };
            document.addEventListener('click', close);
        }, 0);
    },

    buildRatingWidget(summary, mediaType, mediaId) {
        return `<div class="rating-widget" data-mt="${mediaType}" data-mid="${mediaId}">
            ${this._buildRatingWidgetInner(summary || {}, mediaType, mediaId)}
        </div>`;
    },

    _buildRatingWidgetInner(summary, mediaType, mediaId) {
        const avg = summary.average || 0;
        const count = summary.count || 0;
        const userRating = summary.userRating || 0;
        return `
            <div class="rating-widget-community">
                ${count > 0
                    ? `<span class="rating-stars-display">${this._starsHtml(avg, 'display')}</span>
                       <span class="rating-widget-score">${avg.toFixed(1)} / 5</span>
                       <span class="rating-widget-count">(${count} rating${count !== 1 ? 's' : ''})</span>`
                    : `<span style="color:var(--text-muted);font-size:13px">No community ratings yet</span>`}
            </div>
            <div class="rating-widget-user">
                <span class="rating-widget-label">Your rating:</span>
                ${this.buildStarPicker(userRating, mediaType, mediaId)}
                ${userRating > 0 ? `<span class="rating-remove-link" onclick="App.removeRating('${mediaType}', ${mediaId})">Remove</span>` : ''}
            </div>`;
    },

    _starsHtml(avg, mode) {
        const filled = Math.round(avg);
        return Array.from({ length: 5 }, (_, i) =>
            `<span style="color:${i < filled ? 'var(--warning)' : 'var(--text-muted)'};font-size:${mode === 'display' ? '16px' : '22px'}">&#9733;</span>`
        ).join('');
    },

    // ─── Best Rated Page ──────────────────────────────────────────

    async renderBestRated(el) {
        const data = await this.api('ratings/best?limit=20');

        let html = `<div class="page-header"><h1>Best Rated</h1></div>`;

        const hasAny = data && (
            (data.videos?.length > 0) || (data.tracks?.length > 0) ||
            (data.albums?.length > 0) || (data.musicVideos?.length > 0) ||
            (data.pictures?.length > 0) || (data.ebooks?.length > 0)
        );

        if (!hasAny) {
            html += this.emptyState('No rated items yet', 'Rate media from the 3-dot menu on any video, or from within detail views.');
            el.innerHTML = html;
            return;
        }

        // ── Best Rated Movies & TV ──
        if (data.videos && data.videos.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-film"/></svg> Movies & TV Shows</h2>
                    <button class="home-see-all" onclick="App.navigate('movies')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll home-row-wide">`;
            data.videos.forEach(v => {
                const thumbSrc = v.posterPath ? `/videometa/${v.posterPath}` : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
                const dur = this.formatDuration(v.duration);
                const typeLabel = v.mediaType === 'tv' ? 'TV' : v.mediaType === 'documentary' ? 'Doc' : v.mediaType === 'anime' ? 'Anime' : 'Movie';
                const subtitle = v.mediaType === 'tv' && v.seriesName
                    ? `${this.esc(v.seriesName)} S${(v.season||0).toString().padStart(2,'0')}E${(v.episode||0).toString().padStart(2,'0')}`
                    : (v.year ? String(v.year) : '');
                html += `<div class="mv-card home-card" onclick="App.openVideoDetail(${v.id})" data-video-id="${v.id}">
                    <div class="mv-card-thumb" style="position:relative">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        <span class="video-type-badge video-type-${v.mediaType}">${typeLabel}</span>
                        <span class="mv-community-rating">&#9733; ${v.avgRating.toFixed(1)}</span>
                        <button class="mv-card-play" onclick="event.stopPropagation(); App.playVideo(${v.id})">&#9654;</button>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${subtitle}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Best Rated Music Tracks ──
        if (data.tracks && data.tracks.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-music"/></svg> Music Tracks</h2>
                    <button class="home-see-all" onclick="App.navigate('music')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll">`;
            data.tracks.forEach(t => {
                const artSrc = t.albumArtCached ? `/albumart/${t.albumArtCached}` : '';
                const dur = this.formatDuration(t.duration);
                html += `<div class="song-card home-card">
                    <div class="song-card-art" style="position:relative">
                        ${artSrc
                            ? `<img src="${artSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`}
                        <span class="mv-community-rating" style="bottom:4px;left:4px">&#9733; ${t.avgRating.toFixed(1)}</span>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta"><span>${this.esc(t.album)}</span><span>${dur}</span></div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Best Rated Albums ──
        if (data.albums && data.albums.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-music"/></svg> Albums</h2>
                    <button class="home-see-all" onclick="App.navigate('albums')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll">`;
            data.albums.forEach(a => {
                const artSrc = a.coverArtPath ? `/albumart/${a.coverArtPath}` : '';
                html += `<div class="song-card home-card" onclick="App.navigate('albums')">
                    <div class="song-card-art" style="position:relative">
                        ${artSrc
                            ? `<img src="${artSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`}
                        <span class="mv-community-rating" style="bottom:4px;left:4px">&#9733; ${a.avgRating.toFixed(1)}</span>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(a.name)}</div>
                        <div class="song-card-artist">${this.esc(a.artist)}</div>
                        <div class="song-card-meta"><span>${a.trackCount} tracks</span>${a.year ? `<span>${a.year}</span>` : ''}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Best Rated Music Videos ──
        if (data.musicVideos && data.musicVideos.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-video"/></svg> Music Videos</h2>
                    <button class="home-see-all" onclick="App.navigate('musicvideos')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll home-row-wide">`;
            data.musicVideos.forEach(v => {
                const thumbSrc = v.thumbnailPath ? `/mvthumb/${v.thumbnailPath}` : '';
                const dur = this.formatDuration(v.duration);
                html += `<div class="mv-card home-card" onclick="App.openMvDetail(${v.id})" data-mv-id="${v.id}">
                    <div class="mv-card-thumb" style="position:relative">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none">&#127909;</span>`
                            : `<span class="mv-card-placeholder">&#127909;</span>`}
                        <span class="mv-duration-badge">${dur}</span>
                        <span class="mv-community-rating">&#9733; ${v.avgRating.toFixed(1)}</span>
                        <button class="mv-card-play" onclick="event.stopPropagation(); App.playMusicVideo(${v.id})">&#9654;</button>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(v.title)}</div>
                        <div class="mv-card-artist">${this.esc(v.artist)}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Best Rated Pictures ──
        if (data.pictures && data.pictures.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-image"/></svg> Pictures</h2>
                    <button class="home-see-all" onclick="App.navigate('pictures')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll home-row-wide">`;
            data.pictures.forEach(p => {
                const thumbSrc = p.thumbnailPath ? `/picthumb/${p.thumbnailPath}` : '';
                html += `<div class="mv-card home-card" onclick="App.navigate('pictures')">
                    <div class="mv-card-thumb" style="position:relative">
                        ${thumbSrc
                            ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-image"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-image"/></svg></span>`}
                        <span class="mv-community-rating">&#9733; ${p.avgRating.toFixed(1)}</span>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(p.fileName)}</div>
                        <div class="mv-card-artist">${this.esc(p.category)}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        // ── Best Rated eBooks ──
        if (data.ebooks && data.ebooks.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-book"/></svg> eBooks</h2>
                    <button class="home-see-all" onclick="App.navigate('ebooks')">See all &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll home-row-wide">`;
            data.ebooks.forEach(e => {
                const coverSrc = e.coverImage ? `/ebookcover/${e.coverImage}` : '';
                const formatBadge = e.format ? e.format.toLowerCase() : 'epub';
                html += `<div class="mv-card home-card" onclick="App.openEBookDetail(${e.id})">
                    <div class="mv-card-thumb" style="position:relative">
                        ${coverSrc
                            ? `<img src="${coverSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                               <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-book"/></svg></span>`
                            : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-book"/></svg></span>`}
                        <span class="mv-community-rating">&#9733; ${e.avgRating.toFixed(1)}</span>
                        <span class="ebook-format-badge ebook-format-${formatBadge}" style="top:6px;right:6px">${e.format}</span>
                    </div>
                    <div class="mv-card-info">
                        <div class="mv-card-title">${this.esc(e.title)}</div>
                        <div class="mv-card-artist">${this.esc(e.author || 'Unknown Author')}</div>
                    </div>
                </div>`;
            });
            html += '</div><button class="home-nav-btn home-nav-right" onclick="App.homeRowScroll(this, 1)">&#10095;</button></div></div>';
        }

        el.innerHTML = html;
    },

    async saveVideoMetadata() {
        const id = document.getElementById('videoEditId').value;
        const payload = {
            title: document.getElementById('videoEditTitle').value,
            year: parseInt(document.getElementById('videoEditYear').value) || null,
            genre: document.getElementById('videoEditGenre').value,
            director: document.getElementById('videoEditDirector').value,
            cast: document.getElementById('videoEditCast').value,
            overview: document.getElementById('videoEditOverview').value,
            rating: parseFloat(document.getElementById('videoEditRating').value) || 0,
            contentRating: document.getElementById('videoEditContentRating').value,
            safeForChildren: document.getElementById('videoEditSafeForChildren')?.checked ?? false,
            mediaType: document.getElementById('videoEditMediaType').value,
            seriesName: document.getElementById('videoEditSeriesName').value,
            season: parseInt(document.getElementById('videoEditSeason').value) || null,
            episode: parseInt(document.getElementById('videoEditEpisode').value) || null,
            posterImage: this._videoEditPosterData
        };

        try {
            const res = await fetch(`/api/videos/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            const result = await res.json();
            if (result.success) {
                // Save custom genre assignments if the section was shown
                const cgChecks = document.querySelectorAll('.cg-assign-check');
                if (cgChecks.length > 0) {
                    const genreIds = [];
                    cgChecks.forEach(el => { if (el.checked) genreIds.push(el.value); });
                    await this.apiPost(`videos/${id}/custom-genres`, { genreIds });
                }
                this.closeVideoEditModal();
                this.loadVideosPage();
            } else {
                alert('Error saving metadata: ' + (result.error || 'Unknown error'));
            }
        } catch (err) {
            alert('Error: ' + err.message);
        }
    },

    // ─── Equalizer ────────────────────────────────────────────────────

    _initAudioContext() {
        if (this._audioCtx !== null) return;
        try {
            // 'playback' latency hint allows larger buffers → fewer buffer underruns / crackling
            this._audioCtx = new (window.AudioContext || window.webkitAudioContext)({ latencyHint: 'playback' });
            this._eqSourceMap = new Map(); // element → MediaElementSourceNode (created once per element)
            this._eqFilters = this._eqFreqs.map((freq, i) => {
                const f = this._audioCtx.createBiquadFilter();
                f.type = this._eqTypes[i];
                f.frequency.value = freq;
                // Shelf filters need Q = 0.7071 (1/√2) for a flat passband; higher Q creates a
                // resonant bump at the shelf edge which causes coloration and distortion at high gains
                f.Q.value = (this._eqTypes[i] === 'lowshelf' || this._eqTypes[i] === 'highshelf') ? 0.7071 : 1.41;
                f.gain.value = this._eqEnabled ? this._eqBands[i] : 0;
                return f;
            });
            for (let i = 0; i < this._eqFilters.length - 1; i++) this._eqFilters[i].connect(this._eqFilters[i + 1]);
            // Soft limiter: prevents digital clipping when boosted EQ bands sum above 0 dBFS
            this._eqLimiter = this._audioCtx.createDynamicsCompressor();
            this._eqLimiter.threshold.value = -3;  // engage at -3 dBFS
            this._eqLimiter.knee.value = 6;         // soft knee — transparent on normal content
            this._eqLimiter.ratio.value = 20;       // near brick-wall above threshold
            this._eqLimiter.attack.value = 0.001;   // 1 ms attack
            this._eqLimiter.release.value = 0.1;    // 100 ms release
            // Insert analyser between limiter and destination — drives the NC visualiser
            this._ncAnalyser = this._audioCtx.createAnalyser();
            this._ncAnalyser.fftSize = 512;
            this._ncAnalyser.smoothingTimeConstant = 0.82;
            this._ncDataArray = new Uint8Array(this._ncAnalyser.frequencyBinCount);
            this._eqFilters[this._eqFilters.length - 1].connect(this._eqLimiter);
            this._eqLimiter.connect(this._ncAnalyser);
            this._ncAnalyser.connect(this._audioCtx.destination);
        } catch (e) {
            this._audioCtx = false;
            console.warn('Web Audio EQ unavailable:', e);
        }
    },

    // Smoothly ramp a filter's gain to avoid click/pop from hard gain steps
    _setEQGain(filter, targetDb) {
        if (!filter) return;
        if (this._audioCtx && this._audioCtx.currentTime !== undefined) {
            filter.gain.setTargetAtTime(targetDb, this._audioCtx.currentTime, 0.015);
        } else {
            filter.gain.value = targetDb;
        }
    },

    connectEQToElement(el) {
        if (!el) return;
        this._initAudioContext();
        if (!this._audioCtx) return;
        if (this._eqConnectedEl === el) {
            if (this._audioCtx.state === 'suspended') this._audioCtx.resume().catch(() => {});
            return;
        }
        try {
            // Detach current source from the filter chain (but keep the node alive for reuse)
            if (this._eqSource) { try { this._eqSource.disconnect(); } catch (e) {} }
            // Reuse an existing source node for this element, or create one for the first time
            let source = this._eqSourceMap.get(el);
            if (!source) {
                source = this._audioCtx.createMediaElementSource(el);
                this._eqSourceMap.set(el, source);
            }
            source.connect(this._eqFilters[0]);
            this._eqSource = source;
            this._eqConnectedEl = el;
            if (this._audioCtx.state === 'suspended') this._audioCtx.resume().catch(() => {});
        } catch (e) {
            console.warn('EQ connect failed:', e);
        }
    },

    initEqualizer() {
        this.connectEQToElement(this.audioPlayer);
    },

    toggleEQPanel() {
        this._eqPanelOpen = !this._eqPanelOpen;
        let panel = document.getElementById('eq-panel');
        if (this._eqPanelOpen) {
            if (!panel) { this._renderEQPanel(); panel = document.getElementById('eq-panel'); }
            panel.style.display = 'flex';
            document.getElementById('btn-eq').classList.add('eq-active');
            const pageBtn = document.getElementById('btn-eq-page');
            if (pageBtn) pageBtn.style.color = 'var(--accent)';
            setTimeout(() => this.drawEQCurve(), 50);
        } else {
            if (panel) panel.style.display = 'none';
            document.getElementById('btn-eq').classList.remove('eq-active');
            const pageBtn = document.getElementById('btn-eq-page');
            if (pageBtn) pageBtn.style.color = '';
        }
    },

    _renderEQPanel() {
        const freqLabels = ['60Hz', '170', '310', '600', '1k', '3k', '6k', '12k'];
        const macroNames = ['Bass', 'Warmth', 'Clarity', 'Presence', 'Treble'];
        const presetOptions = Object.keys(this._eqPresets).map(n =>
            `<option value="${n}"${n === this._eqPreset ? ' selected' : ''}>${n}</option>`
        ).join('') + `<option value="Custom"${this._eqPreset === 'Custom' ? ' selected' : ''}>${this.t('player.eqCustom', 'Custom')}</option>`;

        const macroRows = macroNames.map(name =>
            `<div class="eq-macro-row">
                <span class="eq-macro-label">${this.t('player.eq' + name, name)}</span>
                <input type="range" class="eq-macro-slider" min="-12" max="12" value="0" data-prev="0"
                       oninput="App.applyEQMacro('${name}',parseFloat(this.value),parseFloat(this.dataset.prev||0));this.dataset.prev=this.value">
            </div>`
        ).join('');

        const bandSliders = this._eqBands.map((dB, i) =>
            `<div class="eq-band">
                <div class="eq-band-db">${dB > 0 ? '+' : ''}${dB}</div>
                <input type="range" class="eq-band-slider" min="-12" max="12" value="${dB}"
                       oninput="App.setEQBand(${i},parseFloat(this.value))">
                <div class="eq-band-freq">${freqLabels[i]}</div>
            </div>`
        ).join('');

        const powerClass = this._eqEnabled ? 'on' : 'off';
        const powerLabel = this._eqEnabled ? 'ON' : 'OFF';

        document.body.insertAdjacentHTML('beforeend',
            `<div id="eq-panel" style="display:flex;flex-direction:column">
                <div class="eq-header">
                    <span class="eq-header-title">
                        <svg style="width:16px;height:16px"><use href="#icon-equalizer"/></svg>
                        ${this.t('player.equalizer', 'Equalizer')}
                    </span>
                    <select class="eq-preset-select" onchange="App.applyEQPreset(this.value)">${presetOptions}</select>
                    <button class="eq-power-btn ${powerClass}" id="eq-power-btn"
                            onclick="App.setEQEnabled(!App._eqEnabled)">${powerLabel}</button>
                    <button class="eq-close-btn" onclick="App.toggleEQPanel()">&#x2715;</button>
                </div>
                <div class="eq-body">
                    <div class="eq-macros">${macroRows}</div>
                    <div class="eq-right">
                        <div class="eq-canvas-wrap"><canvas id="eq-canvas"></canvas></div>
                        <div class="eq-bands">${bandSliders}</div>
                    </div>
                </div>
            </div>`
        );
    },

    applyEQPreset(name) {
        if (!this._eqPresets[name]) return;
        this._eqPreset = name;
        this._eqBands = [...this._eqPresets[name]];
        this._eqBands.forEach((dB, i) => {
            this._setEQGain(this._eqFilters[i], this._eqEnabled ? dB : 0);
        });
        const panel = document.getElementById('eq-panel');
        if (panel) {
            panel.querySelectorAll('.eq-band').forEach((el, i) => {
                const s = el.querySelector('.eq-band-slider'); if (s) s.value = this._eqBands[i];
                const l = el.querySelector('.eq-band-db'); if (l) l.textContent = (this._eqBands[i] > 0 ? '+' : '') + this._eqBands[i];
            });
            panel.querySelectorAll('.eq-macro-slider').forEach(s => { s.value = 0; s.dataset.prev = '0'; });
            const sel = panel.querySelector('.eq-preset-select'); if (sel) sel.value = name;
        }
        this.drawEQCurve();
        this.saveEQState();
    },

    setEQBand(i, dB) {
        this._eqBands[i] = dB;
        this._setEQGain(this._eqFilters[i], this._eqEnabled ? dB : 0);
        const bands = document.querySelectorAll('.eq-band');
        if (bands[i]) {
            const l = bands[i].querySelector('.eq-band-db');
            if (l) l.textContent = (dB > 0 ? '+' : '') + dB;
        }
        this._eqPreset = 'Custom';
        const sel = document.querySelector('.eq-preset-select'); if (sel) sel.value = 'Custom';
        this.drawEQCurve();
        this.saveEQState();
    },

    applyEQMacro(name, val, prevVal) {
        const macro = this._eqMacros[name]; if (!macro) return;
        const delta = val - prevVal;
        macro.bands.forEach((bi, j) => {
            this._eqBands[bi] = Math.round(Math.max(-12, Math.min(12, this._eqBands[bi] + delta * macro.coeff[j])) * 10) / 10;
            this._setEQGain(this._eqFilters[bi], this._eqEnabled ? this._eqBands[bi] : 0);
        });
        const bands = document.querySelectorAll('.eq-band');
        macro.bands.forEach(bi => {
            if (!bands[bi]) return;
            const s = bands[bi].querySelector('.eq-band-slider'); if (s) s.value = this._eqBands[bi];
            const l = bands[bi].querySelector('.eq-band-db'); if (l) l.textContent = (this._eqBands[bi] > 0 ? '+' : '') + this._eqBands[bi];
        });
        this._eqPreset = 'Custom';
        const sel = document.querySelector('.eq-preset-select'); if (sel) sel.value = 'Custom';
        this.drawEQCurve();
        this.saveEQState();
    },

    setEQEnabled(bool) {
        this._eqEnabled = !!bool;
        this._eqFilters.forEach((f, i) => { this._setEQGain(f, this._eqEnabled ? this._eqBands[i] : 0); });
        const btn = document.getElementById('eq-power-btn');
        if (btn) { btn.textContent = this._eqEnabled ? 'ON' : 'OFF'; btn.className = 'eq-power-btn ' + (this._eqEnabled ? 'on' : 'off'); }
        this.drawEQCurve();
        this.saveEQState();
    },

    drawEQCurve() {
        const canvas = document.getElementById('eq-canvas');
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        const w = canvas.clientWidth || 400;
        const h = canvas.clientHeight || 140;
        canvas.width = w; canvas.height = h;
        ctx.clearRect(0, 0, w, h);

        // Grid lines
        ctx.strokeStyle = 'rgba(255,255,255,0.06)'; ctx.lineWidth = 1;
        [-12, -6, 0, 6, 12].forEach(db => {
            const y = h * (1 - (db + 15) / 30);
            ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke();
        });
        const y0 = h * (1 - 15 / 30); // 0dB line
        ctx.strokeStyle = 'rgba(255,255,255,0.12)';
        ctx.beginPath(); ctx.moveTo(0, y0); ctx.lineTo(w, y0); ctx.stroke();

        const accent = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#4d8bf5';

        if (!this._eqEnabled) {
            ctx.strokeStyle = 'rgba(150,150,150,0.5)'; ctx.lineWidth = 2;
            ctx.beginPath(); ctx.moveTo(0, y0); ctx.lineTo(w, y0); ctx.stroke();
            return;
        }

        const N = 200;
        const freqs = new Float32Array(N);
        for (let i = 0; i < N; i++) freqs[i] = 20 * Math.pow(1000, i / (N - 1));

        const combined = new Float32Array(N).fill(1);
        if (this._eqFilters.length > 0) {
            const mag = new Float32Array(N); const ph = new Float32Array(N);
            this._eqFilters.forEach(f => { f.getFrequencyResponse(freqs, mag, ph); for (let i = 0; i < N; i++) combined[i] *= mag[i]; });
        } else {
            this._eqFreqs.forEach((cf, bi) => {
                const g = this._eqBands[bi]; if (g === 0) return;
                const lcf = Math.log10(cf); const sigma = 0.3;
                for (let i = 0; i < N; i++) {
                    const d = (Math.log10(freqs[i]) - lcf) / sigma;
                    combined[i] *= Math.pow(10, g * Math.exp(-0.5 * d * d) / 20);
                }
            });
        }

        const pts = [];
        for (let i = 0; i < N; i++) {
            const x = i / (N - 1) * w;
            const db = Math.max(-15, Math.min(15, 20 * Math.log10(Math.max(combined[i], 1e-6))));
            pts.push({ x, y: h * (1 - (db + 15) / 30) });
        }

        // Gradient fill
        ctx.beginPath();
        ctx.moveTo(pts[0].x, pts[0].y);
        pts.forEach(p => ctx.lineTo(p.x, p.y));
        ctx.lineTo(w, h); ctx.lineTo(0, h); ctx.closePath();
        ctx.globalAlpha = 0.2; ctx.fillStyle = accent; ctx.fill(); ctx.globalAlpha = 1;

        // Curve
        ctx.beginPath();
        ctx.moveTo(pts[0].x, pts[0].y);
        pts.forEach(p => ctx.lineTo(p.x, p.y));
        ctx.strokeStyle = accent; ctx.lineWidth = 2;
        ctx.shadowBlur = 8; ctx.shadowColor = accent; ctx.stroke(); ctx.shadowBlur = 0;

        // Band dots
        const logMin = Math.log10(20); const logMax = Math.log10(20000);
        this._eqFreqs.forEach((freq, i) => {
            const x = ((Math.log10(freq) - logMin) / (logMax - logMin)) * w;
            const db = Math.max(-15, Math.min(15, this._eqBands[i]));
            const y = h * (1 - (db + 15) / 30);
            ctx.beginPath(); ctx.arc(x, y, 4, 0, Math.PI * 2);
            ctx.fillStyle = accent; ctx.fill();
            ctx.strokeStyle = '#1e1e1e'; ctx.lineWidth = 1.5; ctx.stroke();
        });
    },

    saveEQState() {
        try { localStorage.setItem('nexusm-eq', JSON.stringify({ enabled: this._eqEnabled, preset: this._eqPreset, bands: this._eqBands })); } catch (e) {}
    },

    loadEQState() {
        try {
            const raw = localStorage.getItem('nexusm-eq');
            if (!raw) return;
            const s = JSON.parse(raw);
            if (typeof s.enabled === 'boolean') this._eqEnabled = s.enabled;
            if (typeof s.preset === 'string') this._eqPreset = s.preset;
            if (Array.isArray(s.bands) && s.bands.length === 8) this._eqBands = s.bands;
        } catch (e) {}
    },

    // ─── Go Big Mode — TV / Big-Screen Interface ──────────────────────────────

    startGoBigMode() {
        if (this._gbActive) return;
        if (this._gbIsMobile()) {
            // Send request for the desktop to start Go Big; remote appears once desktop announces
            fetch('/api/gobig/request', { method: 'POST' }).catch(() => {});
            // Ensure status polling is running so remote auto-opens when desktop is ready
            if (!this._gbRemoteCheckTimer) this._gbStartRemoteCheck();
            // Update floating button state if visible
            const btn = document.getElementById('gb-mobile-start');
            if (btn) { btn.disabled = true; btn.textContent = 'Starting…'; }
            return;
        }
        this._gbStopRequestCheck();
        this._gbActive = true;
        sessionStorage.setItem('nexusm-gobig', '1');
        document.body.style.overflow = 'hidden';

        const ol = document.createElement('div');
        ol.id = 'gb-overlay';
        ol.innerHTML = `
            <div id="gb-loading"><div class="gb-loading-dots"><span></span><span></span><span></span></div></div>
            <div id="gb-detail" style="display:none"></div>
            <div id="gb-music-player" style="display:none">
                <div id="gbm-bg"></div>
                <div id="gbm-bg-scrim"></div>
                <button class="gbm-close-btn" onclick="App._gbMusicPlayerClose()">&#10005;</button>
                <div id="gbm-inner">
                    <div class="gbm-label">NOW PLAYING</div>
                    <div id="gbm-art-wrap">
                        <img id="gbm-art" src="" alt="" style="display:none">
                        <div id="gbm-art-ph"><svg style="width:64px;height:64px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-music-note"/></svg></div>
                    </div>
                    <div id="gbm-title"></div>
                    <div id="gbm-meta"></div>
                    <div id="gbm-progress-wrap">
                        <div id="gbm-bar" onclick="App._gbMusicPlayerSeek(event)">
                            <div id="gbm-track"></div>
                            <div id="gbm-fill"></div>
                            <div id="gbm-thumb"></div>
                        </div>
                        <div id="gbm-times"><span id="gbm-cur">0:00</span><span id="gbm-dur">0:00</span></div>
                    </div>
                    <div id="gbm-controls">
                        <button id="gbm-shuffle-btn" onclick="App._gbMusicToggleShuffle()" title="Shuffle"><svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="16 3 21 3 21 8"/><line x1="4" y1="20" x2="21" y2="3"/><polyline points="21 16 21 21 16 21"/><line x1="15" y1="15" x2="21" y2="21"/></svg></button>
                        <button id="gbm-prev-btn" onclick="App._gbMusicPlayerPrev()" title="Previous"><svg width="40" height="40" viewBox="0 0 24 24" fill="currentColor"><path d="M6 6h2v12H6zm3.5 6 8.5 6V6z"/></svg></button>
                        <button id="gbm-play-btn" onclick="App._gbMusicPlayerToggle()"><svg id="gbm-play-icon" width="44" height="44" viewBox="0 0 24 24" fill="currentColor"><path d="M6 5h4v14H6zM14 5h4v14h-4z"/></svg></button>
                        <button id="gbm-next-btn" onclick="App._gbMusicPlayerNext()" title="Next"><svg width="40" height="40" viewBox="0 0 24 24" fill="currentColor"><path d="M6 18l8.5-6L6 6v12zM16 6h2v12h-2z"/></svg></button>
                        <button id="gbm-repeat-btn" onclick="App._gbMusicToggleRepeat()" title="Repeat" style="position:relative"><svg id="gbm-repeat-icon" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/></svg><span id="gbm-repeat-one-badge" style="display:none;position:absolute;bottom:6px;right:6px;font-size:9px;font-weight:900;background:#22c55e;color:#000;border-radius:3px;padding:1px 3px;line-height:1.2">1</span></button>
                    </div>
                    <div id="gbm-next-info"></div>
                </div>
                <audio id="gbm-audio" preload="none"></audio>
            </div>
            <div id="gb-scroll">
                <div id="gb-hero">
                    <div id="gb-hero-bg"></div>
                    <div id="gb-hero-overlay"></div>
                    <div id="gb-hero-content">
                        <div class="gb-hero-badge">Featured</div>
                        <h1 id="gb-hero-title"></h1>
                        <div id="gb-hero-meta"></div>
                        <p id="gb-hero-overview"></p>
                        <div id="gb-hero-btns">
                            <button class="gb-hero-btn gb-hero-btn-primary" id="gb-hero-play">&#9654; Watch Now</button>
                            <button class="gb-hero-btn gb-hero-btn-secondary" id="gb-hero-info">Details</button>
                        </div>
                    </div>
                    <div id="gb-hero-dots"></div>
                </div>
                <div id="gb-rows"></div>
                <div id="gb-footer">
                    <button id="gb-exit-btn" onclick="App.stopGoBigMode()">
                        <svg style="width:15px;height:15px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;flex-shrink:0" viewBox="0 0 24 24">
                            <path d="M18.36 6.64a9 9 0 1 1-12.73 0"/><line x1="12" y1="2" x2="12" y2="12"/>
                        </svg>
                        Exit Go Big Mode
                    </button>
                </div>
            </div>
            <div id="gb-topbar">
                <span id="gb-logo">Nexus<span style="color:#22c55e">M</span></span>
                <div id="gb-topbar-spacer"></div>
                <button id="gb-search-btn" onclick="App._gbOpenSearch()" title="Search (S)"><svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;margin-right:5px;vertical-align:-1px"><use href="#icon-search"/></svg>Search</button>
                <button id="gb-top-exit" onclick="App._gbTopExit()">&#10005;&ensp;Exit</button>
            </div>
            <div id="gb-search" style="display:none">
                <div class="gb-search-bar">
                    <svg style="width:22px;height:22px;stroke:rgba(255,255,255,.35);fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;flex-shrink:0"><use href="#icon-search"/></svg>
                    <input id="gb-search-input" type="search" placeholder="Search movies, shows, songs, actors..." autocomplete="off" spellcheck="false">
                    <button class="gb-search-close-btn" onclick="App._gbCloseSearch()">&#10005;</button>
                </div>
                <div id="gb-search-results"><div class="gbs-empty">Start typing to search...</div></div>
            </div>`;
        document.body.appendChild(ol);
        this._gbOverlay = ol;

        this._gbFocusZone = 'hero';
        this._gbFocusRow = 0;
        this._gbFocusCard = 0;
        this._gbFocusHeroBtn = 0;

        this._gbKeyHandler = (e) => this._gbHandleKey(e);
        document.addEventListener('keydown', this._gbKeyHandler);

        this._gbStartPolling();
        this._gbLoadAndRender();
    },

    _gbTopExit() {
        const detail = this._gbOverlay?.querySelector('#gb-detail');
        if (detail && detail.style.display === 'block') this._gbBackFromDetail();
        else this.stopGoBigMode();
    },

    stopGoBigMode() {
        if (!this._gbActive) return;
        this._gbActive = false;
        sessionStorage.removeItem('nexusm-gobig');
        document.body.style.overflow = '';
        if (this._gbHeroTimer)  { clearInterval(this._gbHeroTimer); this._gbHeroTimer = null; }
        if (this._gbKeyHandler) { document.removeEventListener('keydown', this._gbKeyHandler); this._gbKeyHandler = null; }
        this._gbStopPolling();
        if (this._gbFsMouseMove && this._gbOverlay) { this._gbOverlay.removeEventListener('mousemove', this._gbFsMouseMove); this._gbFsMouseMove = null; }
        clearTimeout(this._gbFsHoverTimer);
        this._gbHideFullscreenPrompt();
        // Stop any video or audio started while in Go Big mode
        const gbVideo = document.getElementById('gbd-video-player');
        if (gbVideo) { this.stopVideoStream(gbVideo); gbVideo.pause(); gbVideo.src = ''; }
        clearInterval(this._gbMusicProgressInterval);
        this._gbMusicProgressInterval = null;
        const gbmAudio = this._gbOverlay?.querySelector('#gbm-audio');
        if (gbmAudio) { gbmAudio.pause(); gbmAudio.src = ''; gbmAudio.load(); }
        if (this._gbTranscodeId) {
            const tid = this._gbTranscodeId; this._gbTranscodeId = null;
            this.apiPost(`stop-transcode/${tid}`, {}).catch(() => {});
        }
        if (this._gbOverlay)    { this._gbOverlay.remove(); this._gbOverlay = null; }
        this._gbHeroItems = null;
        this._gbMovies = this._gbTv = this._gbDocos = this._gbMvs = this._gbAnime = null;
        // Resume listening for mobile Go Big requests
        this._gbStartRequestCheck();
    },

    async _gbLoadAndRender() {
        try {
            const [vidData, mvData, plData, animeData] = await Promise.all([
                this.api('videos?sort=recent&limit=48&grouped=true').catch(() => null),
                this.api('musicvideos?sort=recent&limit=32').catch(() => null),
                this.api('playlists').catch(() => null),
                this.api('videos?mediaType=anime&sort=recent&limit=32&grouped=true').catch(() => null),
            ]);
            if (!this._gbOverlay) return;

            const all    = vidData?.items || vidData?.videos || [];
            const movies = all.filter(v => v.type !== 'series' && v.mediaType === 'movie');
            const tv     = all.filter(v => v.type === 'series' || v.mediaType === 'tv');
            const docos  = all.filter(v => v.type !== 'series' && v.mediaType === 'documentary');
            const mvs    = mvData?.videos || [];
            const pls    = Array.isArray(plData) ? plData : [];
            const animeAll = animeData?.items || animeData?.videos || [];
            const anime  = animeAll; // already filtered by mediaType=anime

            this._gbMovies    = movies;
            this._gbTv        = tv;
            this._gbDocos     = docos;
            this._gbMvs       = mvs;
            this._gbPlaylists = pls;
            this._gbAnime     = anime;

            // Hero — pick items that have at least a poster
            this._gbHeroItems = [...movies, ...tv, ...docos, ...anime]
                .filter(v => v.posterPath || v.backdropPath)
                .slice(0, 8);
            this._gbHeroIdx = 0;

            const loading = this._gbOverlay.querySelector('#gb-loading');
            if (loading) loading.style.display = 'none';

            if (this._gbHeroItems.length > 0) {
                this._gbSetHero(this._gbHeroItems[0], false);
                const dots = this._gbOverlay.querySelector('#gb-hero-dots');
                if (dots) {
                    dots.innerHTML = this._gbHeroItems.map((_, i) =>
                        `<div class="gb-hero-dot${i === 0 ? ' active' : ''}" onclick="App._gbGoHero(${i})"></div>`
                    ).join('');
                }
                if (this._gbHeroItems.length > 1) {
                    this._gbHeroTimer = setInterval(() => {
                        if (!this._gbActive) return;
                        this._gbHeroIdx = (this._gbHeroIdx + 1) % this._gbHeroItems.length;
                        this._gbSetHero(this._gbHeroItems[this._gbHeroIdx], true);
                    }, 9000);
                }
            }

            const rowsEl = this._gbOverlay.querySelector('#gb-rows');
            if (!rowsEl) return;
            let html = '';
            if (movies.length) html += this._gbRow('gb-movies',    'Movies',        movies, (v, i) => this._gbVideoCard(v, i, false));
            if (tv.length)     html += this._gbRow('gb-tv',        'TV Shows',      tv,     (v, i) => this._gbVideoCard(v, i, true));
            if (docos.length)  html += this._gbRow('gb-docos',     'Documentaries', docos,  (v, i) => this._gbDocoCard(v, i));
            if (anime.length)  html += this._gbRow('gb-anime',     'Anime',         anime,  (v, i) => this._gbAnimeCard(v, i));
            if (mvs.length)    html += this._gbRow('gb-mvs',       'Music Videos',  mvs,    (v, i) => this._gbMvCard(v, i));
            if (pls.length)    html += this._gbRow('gb-playlists', 'Playlists',     pls,    (v, i) => this._gbPlaylistCard(v, i));
            rowsEl.innerHTML = html;
            // Apply initial keyboard focus to the first hero button
            setTimeout(() => this._gbApplyFocus(), 50);

        } catch (err) {
            console.error('Go Big load error:', err);
            const ld = this._gbOverlay?.querySelector('#gb-loading .gb-loading-dots');
            if (ld) ld.innerHTML = '<span style="width:auto;height:auto;font-size:13px;letter-spacing:.1em;opacity:.5">Could not load content</span>';
        }
    },

    _gbGoHero(idx) {
        if (!this._gbHeroItems || idx < 0 || idx >= this._gbHeroItems.length) return;
        this._gbHeroIdx = idx;
        this._gbSetHero(this._gbHeroItems[idx], true);
        if (this._gbHeroTimer) { clearInterval(this._gbHeroTimer); this._gbHeroTimer = null; }
        if (this._gbHeroItems.length > 1) {
            this._gbHeroTimer = setInterval(() => {
                if (!this._gbActive) return;
                this._gbHeroIdx = (this._gbHeroIdx + 1) % this._gbHeroItems.length;
                this._gbSetHero(this._gbHeroItems[this._gbHeroIdx], true);
            }, 9000);
        }
    },

    _gbSetHero(item, fade) {
        const ol = this._gbOverlay;
        if (!ol) return;
        const bg       = ol.querySelector('#gb-hero-bg');
        const titleEl  = ol.querySelector('#gb-hero-title');
        const metaEl   = ol.querySelector('#gb-hero-meta');
        const overEl   = ol.querySelector('#gb-hero-overview');
        const playBtn  = ol.querySelector('#gb-hero-play');
        const infoBtn  = ol.querySelector('#gb-hero-info');

        const isTV  = item.type === 'series' || item.mediaType === 'tv';
        const imgSrc = item.backdropPath ? `/videometa/${item.backdropPath}`
                     : item.posterPath   ? `/videometa/${item.posterPath}` : '';

        if (bg) {
            const applyBg = () => {
                if (!this._gbOverlay) return;
                if (imgSrc) {
                    bg.style.backgroundImage = `url(${JSON.stringify(imgSrc)})`;
                    bg.classList.toggle('gb-bg-poster', !item.backdropPath && !!item.posterPath);
                } else {
                    bg.style.backgroundImage = 'none';
                    bg.classList.remove('gb-bg-poster');
                }
                if (fade) bg.style.opacity = '1';
            };
            if (fade) { bg.style.opacity = '0'; setTimeout(applyBg, 320); }
            else applyBg();
        }

        if (titleEl)  titleEl.textContent = item.seriesName || item.title || '';

        const parts = [];
        if (item.year)          parts.push(item.year);
        if (item.genre)         parts.push(item.genre.split(',')[0].trim());
        if (item.rating)        parts.push(item.rating);
        if (item.episodeCount)  parts.push(`${item.episodeCount} episodes`);
        else if (item.duration) parts.push(this.formatDuration(item.duration));
        if (metaEl) metaEl.textContent = parts.join('\u2002\u00B7\u2002');

        if (overEl) overEl.textContent = item.overview || '';

        const open = () => {
            if (item.seriesName && (isTV || item.mediaType === 'anime'))
                this._gbShowSeriesDetail(item.seriesName, item.mediaType === 'anime' ? 'anime' : 'tv');
            else if (item.id) this._gbShowDetail(item.id);
        };
        if (playBtn) playBtn.onclick = open;
        if (infoBtn) infoBtn.onclick = open;

        ol.querySelectorAll('.gb-hero-dot').forEach((d, i) => d.classList.toggle('active', i === this._gbHeroIdx));
    },

    _gbRow(id, title, items, cardFn) {
        const cards = items.map((v, i) => cardFn(v, i)).join('');
        return `<div class="gb-row" id="${id}">
            <div class="gb-row-header">
                <span class="gb-row-title">${title}</span>
                <span class="gb-row-count">${items.length} title${items.length !== 1 ? 's' : ''}</span>
            </div>
            <div class="gb-row-track">
                <button class="gb-nav-btn gb-nav-left" onclick="App._gbScroll(this,-1)">&#10094;</button>
                <div class="gb-row-scroll">${cards}</div>
                <button class="gb-nav-btn gb-nav-right" onclick="App._gbScroll(this,1)">&#10095;</button>
            </div>
        </div>`;
    },

    _gbScroll(btn, dir) {
        const sc = btn.closest('.gb-row-track')?.querySelector('.gb-row-scroll');
        if (sc) sc.scrollBy({ left: dir * sc.clientWidth * 0.75, behavior: 'smooth' });
    },

    _gbVideoCard(v, idx, isTV) {
        const imgSrc = v.posterPath  ? `/videometa/${v.posterPath}`
                     : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
        const label  = v.seriesName || v.title || '';
        const sub    = isTV
            ? `${v.seasonCount || '?'} season${(v.seasonCount || 0) !== 1 ? 's' : ''}`
            : (v.year ? String(v.year) : '');
        const fn     = isTV ? `App._gbOpenTv(${idx})` : `App._gbOpenMovie(${idx})`;
        return `<div class="gb-card-movie" onclick="${fn}" tabindex="0">
            <div class="gb-poster-wrap">
                ${imgSrc
                    ? `<img class="gb-poster" src="${imgSrc}" loading="lazy" alt=""
                           onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
                       <div class="gb-poster-ph" style="display:none"><svg style="width:40px;height:40px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></div>`
                    : `<div class="gb-poster-ph"><svg style="width:40px;height:40px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></div>`}
                <div class="gb-hover-play">&#9654;</div>
            </div>
            <div class="gb-card-label">
                <div class="gb-card-label-title">${this.esc(label)}</div>
                ${sub ? `<div class="gb-card-label-sub">${this.esc(sub)}</div>` : ''}
            </div>
        </div>`;
    },

    _gbDocoCard(v, idx) {
        const imgSrc = v.posterPath  ? `/videometa/${v.posterPath}`
                     : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
        const label  = v.title || '';
        const sub    = v.year ? String(v.year) : '';
        return `<div class="gb-card-movie" onclick="App._gbOpenDoco(${idx})" tabindex="0">
            <div class="gb-poster-wrap">
                ${imgSrc
                    ? `<img class="gb-poster" src="${imgSrc}" loading="lazy" alt=""
                           onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
                       <div class="gb-poster-ph" style="display:none">&#127902;</div>`
                    : `<div class="gb-poster-ph">&#127902;</div>`}
                <div class="gb-hover-play">&#9654;</div>
            </div>
            <div class="gb-card-label">
                <div class="gb-card-label-title">${this.esc(label)}</div>
                ${sub ? `<div class="gb-card-label-sub">${this.esc(sub)}</div>` : ''}
            </div>
        </div>`;
    },

    _gbAnimeCard(v, idx) {
        const imgSrc = v.posterPath   ? `/videometa/${v.posterPath}`
                     : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
        const label  = v.seriesName || v.title || '';
        const sub    = v.type === 'series'
            ? `${v.seasonCount || '?'} season${(v.seasonCount || 0) !== 1 ? 's' : ''}`
            : (v.year ? String(v.year) : '');
        return `<div class="gb-card-movie" onclick="App._gbOpenAnime(${idx})" tabindex="0">
            <div class="gb-poster-wrap">
                ${imgSrc
                    ? `<img class="gb-poster" src="${imgSrc}" loading="lazy" alt=""
                           onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
                       <div class="gb-poster-ph" style="display:none">&#9733;</div>`
                    : `<div class="gb-poster-ph">&#9733;</div>`}
                <div class="gb-hover-play">&#9654;</div>
            </div>
            <div class="gb-card-label">
                <div class="gb-card-label-title">${this.esc(label)}</div>
                ${sub ? `<div class="gb-card-label-sub">${this.esc(sub)}</div>` : ''}
            </div>
        </div>`;
    },

    _gbMvCard(v, idx) {
        const src = v.thumbnailPath ? `/mvthumb/${v.thumbnailPath}` : '';
        return `<div class="gb-card-mv" onclick="App._gbOpenMv(${idx})" tabindex="0">
            <div class="gb-thumb-wrap">
                ${src
                    ? `<img class="gb-thumb" src="${src}" loading="lazy" alt=""
                           onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
                       <div class="gb-thumb-ph" style="display:none">&#127925;</div>`
                    : `<div class="gb-thumb-ph">&#127925;</div>`}
                <div class="gb-hover-play">&#9654;</div>
            </div>
            <div class="gb-card-label">
                <div class="gb-card-label-title">${this.esc(v.title || '')}</div>
                <div class="gb-card-label-sub">${this.esc(v.artist || '')}</div>
            </div>
        </div>`;
    },

    _gbPlaylistCard(pl, idx) {
        return `<div class="gb-card-movie gb-card-playlist" tabindex="0" onclick="App._gbOpenPlaylist(${idx})" onkeydown="if(event.key==='Enter')App._gbOpenPlaylist(${idx})">
            <div class="gb-pl-art">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/>
                </svg>
            </div>
            <div class="gb-card-label">
                <div class="gb-card-label-title">${this.esc(pl.name || 'Playlist')}</div>
                <div class="gb-card-label-sub">${pl.trackCount || 0} track${pl.trackCount !== 1 ? 's' : ''}</div>
            </div>
        </div>`;
    },

    async _gbOpenPlaylist(idx) {
        const pl = this._gbPlaylists?.[idx];
        if (!pl) return;
        const data = await this.api(`playlists/${pl.id}`).catch(() => null);
        if (!data) return;
        const entries = (data.playlistTracks || []).filter(pt => pt.track);
        const tracks = entries.map(pt => pt.track);
        if (!tracks.length) return;
        this._gbOpenMusicPlayer(0, tracks);
    },

    // ─── Go Big Keyboard Navigation ──────────────────────────────────────────

    _gbGetRowIds() {
        const rows = [];
        if (this._gbMovies?.length)    rows.push('gb-movies');
        if (this._gbTv?.length)        rows.push('gb-tv');
        if (this._gbDocos?.length)     rows.push('gb-docos');
        if (this._gbAnime?.length)     rows.push('gb-anime');
        if (this._gbMvs?.length)       rows.push('gb-mvs');
        if (this._gbPlaylists?.length) rows.push('gb-playlists');
        return rows;
    },

    _gbGetCards(rowIdx) {
        const rowIds = this._gbGetRowIds();
        if (rowIdx < 0 || rowIdx >= rowIds.length) return [];
        const rowEl = this._gbOverlay?.querySelector(`#${rowIds[rowIdx]} .gb-row-scroll`);
        if (!rowEl) return [];
        return Array.from(rowEl.querySelectorAll('.gb-card-movie, .gb-card-mv'));
    },

    _gbClearFocus() {
        this._gbOverlay?.querySelectorAll('.gb-focused').forEach(el => el.classList.remove('gb-focused'));
    },

    _gbApplyFocus() {
        this._gbClearFocus();
        const ol = this._gbOverlay;
        if (!ol) return;
        if (this._gbFocusZone === 'hero') {
            const btns = ol.querySelectorAll('.gb-hero-btn');
            const btn = btns[this._gbFocusHeroBtn];
            if (btn) {
                btn.classList.add('gb-focused');
                btn.focus({ preventScroll: true });
                btn.scrollIntoView({ block: 'nearest', inline: 'nearest' });
            }
        } else {
            const cards = this._gbGetCards(this._gbFocusRow);
            const card = cards[this._gbFocusCard];
            if (card) {
                card.classList.add('gb-focused');
                card.focus({ preventScroll: true });
                card.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'smooth' });
            }
        }
    },

    // Returns all keyboard-navigable elements inside the open detail panel:
    // [0] Back button, [1] Play button (if present), [2..] episode rows
    _gbGetDetailItems() {
        const detail = this._gbOverlay?.querySelector('#gb-detail');
        if (!detail || detail.style.display !== 'block') return [];
        const items = [];
        const back = detail.querySelector('.gbd-back-btn');
        if (back) items.push(back);
        const play = detail.querySelector('#gbd-play-btn');
        if (play) items.push(play);
        detail.querySelectorAll('.gbd-ep-row').forEach(r => items.push(r));
        return items;
    },

    _gbApplyDetailFocus() {
        const items = this._gbGetDetailItems();
        items.forEach(el => el.classList.remove('gb-focused'));
        const item = items[this._gbDetailFocusIdx];
        if (!item) return;
        item.classList.add('gb-focused');
        item.focus({ preventScroll: true });
        item.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    },

    _gbHandleKey(e) {
        if (!this._gbActive || !this._gbOverlay) return;
        // If music player is open, handle its controls
        const _gbMusicEl = this._gbOverlay.querySelector('#gb-music-player');
        if (_gbMusicEl && _gbMusicEl.style.display !== 'none') {
            switch (e.key) {
                case ' ':          e.preventDefault(); this._gbMusicPlayerToggle(); break;
                case 'ArrowLeft':  e.preventDefault(); this._gbMusicPlayerPrev();   break;
                case 'ArrowRight': e.preventDefault(); this._gbMusicPlayerNext();   break;
                case 'Escape':     e.preventDefault(); this._gbMusicPlayerClose();  break;
            }
            return;
        }

        // If search overlay is open, handle navigation within it
        if (this._gbSearchActive) {
            const input = this._gbOverlay?.querySelector('#gb-search-input');
            const isInInput = document.activeElement === input;

            if (e.key === 'Escape') {
                e.preventDefault();
                if (!isInInput) { this._gbSearchFocusIdx = -1; input?.focus(); }
                else this._gbCloseSearch();
                return;
            }

            const items = this._gbGetSearchItems();

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (isInInput) {
                    if (items.length) { this._gbSearchFocusIdx = 0; this._gbApplySearchFocus(items); }
                } else {
                    this._gbSearchFocusIdx = Math.min((this._gbSearchFocusIdx ?? 0) + 1, items.length - 1);
                    this._gbApplySearchFocus(items);
                }
                return;
            }

            if (e.key === 'ArrowUp') {
                if (isInInput) return; // let cursor stay in input
                e.preventDefault();
                if ((this._gbSearchFocusIdx ?? 0) <= 0) { this._gbSearchFocusIdx = -1; input?.focus(); }
                else { this._gbSearchFocusIdx--; this._gbApplySearchFocus(items); }
                return;
            }

            if (e.key === 'ArrowRight' && !isInInput) {
                e.preventDefault();
                this._gbSearchFocusIdx = Math.min((this._gbSearchFocusIdx ?? 0) + 1, items.length - 1);
                this._gbApplySearchFocus(items);
                return;
            }

            if (e.key === 'ArrowLeft' && !isInInput) {
                e.preventDefault();
                if ((this._gbSearchFocusIdx ?? 0) <= 0) { this._gbSearchFocusIdx = -1; input?.focus(); }
                else { this._gbSearchFocusIdx--; this._gbApplySearchFocus(items); }
                return;
            }

            if (e.key === 'Enter' && !isInInput) {
                e.preventDefault();
                items[this._gbSearchFocusIdx]?.click();
                return;
            }

            return;
        }
        // Global shortcuts — work in any sub-state
        if (e.key === 'f' || e.key === 'F') {
            e.preventDefault();
            if (this._ncActive) {
                // Nightclub Mode is on top — fullscreen its overlay, not the Go Big video player
                if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
                else document.getElementById('nc-overlay')?.requestFullscreen({ navigationUI: 'hide' }).catch(() => {});
            } else {
                this._gbToggleFullscreen();
            }
            return;
        }
        if (e.key === ' ') {
            const vid = document.getElementById('gbd-video-player');
            if (vid && (vid.src || vid._hlsInstance)) { e.preventDefault(); vid.paused ? vid.play().catch(() => {}) : vid.pause(); return; }
        }
        // If detail panel is visible: full navigation within it
        const detail = this._gbOverlay.querySelector('#gb-detail');
        if (detail && detail.style.display === 'block') {
            const items = this._gbGetDetailItems();
            switch (e.key) {
                case 'Escape':
                case 'ArrowLeft':
                    e.preventDefault();
                    this._gbBackFromDetail();
                    break;
                case 'ArrowDown':
                    e.preventDefault();
                    if (this._gbDetailFocusIdx < items.length - 1) {
                        this._gbDetailFocusIdx++;
                        this._gbApplyDetailFocus();
                    }
                    break;
                case 'ArrowUp':
                    e.preventDefault();
                    if (this._gbDetailFocusIdx > 0) {
                        this._gbDetailFocusIdx--;
                        this._gbApplyDetailFocus();
                    }
                    break;
                case 'Enter': {
                    e.preventDefault();
                    const item = items[this._gbDetailFocusIdx];
                    if (item && !item.disabled) item.click();
                    break;
                }
            }
            return;
        }

        // 'S' opens search
        if (e.key === 's' || e.key === 'S') { e.preventDefault(); this._gbOpenSearch(); return; }

        const rowIds = this._gbGetRowIds();
        const numRows = rowIds.length;

        switch (e.key) {
            case 'Escape':
                e.preventDefault();
                this.stopGoBigMode();
                break;

            case 'ArrowRight':
                e.preventDefault();
                if (this._gbFocusZone === 'hero') {
                    const heroBtns = this._gbOverlay.querySelectorAll('.gb-hero-btn');
                    this._gbFocusHeroBtn = Math.min(this._gbFocusHeroBtn + 1, heroBtns.length - 1);
                } else {
                    const cards = this._gbGetCards(this._gbFocusRow);
                    if (this._gbFocusCard < cards.length - 1) this._gbFocusCard++;
                }
                this._gbApplyFocus();
                break;

            case 'ArrowLeft':
                e.preventDefault();
                if (this._gbFocusZone === 'hero') {
                    this._gbFocusHeroBtn = Math.max(this._gbFocusHeroBtn - 1, 0);
                } else {
                    if (this._gbFocusCard > 0) this._gbFocusCard--;
                }
                this._gbApplyFocus();
                break;

            case 'ArrowDown':
                e.preventDefault();
                if (this._gbFocusZone === 'hero') {
                    if (numRows > 0) {
                        this._gbFocusZone = 'rows';
                        this._gbFocusRow = 0;
                        this._gbFocusCard = 0;
                    }
                } else if (this._gbFocusRow < numRows - 1) {
                    this._gbFocusRow++;
                    const nc = this._gbGetCards(this._gbFocusRow);
                    this._gbFocusCard = Math.min(this._gbFocusCard, Math.max(0, nc.length - 1));
                }
                this._gbApplyFocus();
                break;

            case 'ArrowUp':
                e.preventDefault();
                if (this._gbFocusZone === 'rows') {
                    if (this._gbFocusRow > 0) {
                        this._gbFocusRow--;
                        const nc = this._gbGetCards(this._gbFocusRow);
                        this._gbFocusCard = Math.min(this._gbFocusCard, Math.max(0, nc.length - 1));
                    } else {
                        this._gbFocusZone = 'hero';
                    }
                }
                this._gbApplyFocus();
                break;

            case 'Enter': {
                e.preventDefault();
                if (this._gbFocusZone === 'hero') {
                    const heroBtns = this._gbOverlay.querySelectorAll('.gb-hero-btn');
                    heroBtns[this._gbFocusHeroBtn]?.click();
                } else {
                    const cards = this._gbGetCards(this._gbFocusRow);
                    cards[this._gbFocusCard]?.click();
                }
                break;
            }
        }
    },

    _gbToggleFullscreen() {
        const vid = document.getElementById('gbd-video-player');
        const target = (vid && (vid.src || vid._hlsInstance)) ? vid : this._gbOverlay;
        if (!target) return;
        if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
        else target.requestFullscreen({ navigationUI: 'hide' }).catch(() => {});
    },

    // CSS fake-fullscreen — used by remote command since requestFullscreen() needs a user gesture
    _gbToggleCssFullscreen() {
        const section = document.getElementById('gbd-player-section');
        const ol = this._gbOverlay;
        if (!section || !ol) return;

        const isFs = section.classList.toggle('gb-fake-fs');
        // Add class to overlay root so sibling elements (topbar, tc-info) can be hidden via CSS
        ol.classList.toggle('gb-overlay-fs', isFs);

        // ✕ exit button — appended to ol (#gb-overlay) so it lives outside #gb-detail's stacking context
        let exitBtn = ol.querySelector('.gb-fake-fs-exit');
        if (isFs && !exitBtn) {
            exitBtn = document.createElement('button');
            exitBtn.className = 'gb-fake-fs-exit';
            exitBtn.title = 'Exit fullscreen';
            exitBtn.innerHTML = '&#10005;';
            exitBtn.onclick = () => this._gbToggleCssFullscreen();
            ol.appendChild(exitBtn);
        } else if (!isFs && exitBtn) {
            exitBtn.remove();
        }

        const vidEl = document.getElementById('gbd-video-player');
        const hideUi = () => {
            ol.classList.remove('gb-overlay-fs-hover');
            if (vidEl) vidEl.controls = false;
        };
        const showUi = () => {
            ol.classList.add('gb-overlay-fs-hover');
            if (vidEl) vidEl.controls = true;
            clearTimeout(this._gbFsHoverTimer);
            this._gbFsHoverTimer = setTimeout(hideUi, 3000);
        };

        if (isFs) {
            // Mouse move → briefly reveal topbar + tc-info + video controls
            this._gbFsMouseMove = showUi;
            ol.addEventListener('mousemove', this._gbFsMouseMove);
            // Show UI on entry, auto-hide after 3s
            showUi();
        } else {
            if (this._gbFsMouseMove) { ol.removeEventListener('mousemove', this._gbFsMouseMove); this._gbFsMouseMove = null; }
            clearTimeout(this._gbFsHoverTimer);
            ol.classList.remove('gb-overlay-fs-hover');
            if (vidEl) vidEl.controls = true;
        }
    },

    // ─── Gesture latch — shows a tap-target overlay when the remote requests fullscreen.
    // requestFullscreen() MUST be called from a trusted user gesture (click/touch).
    // The mobile remote's poll handler is not trusted, so we show this overlay and let
    // the user tap the screen — that tap IS a trusted gesture. Automatically dismissed
    // via fullscreenchange (OS keypress path got there first) or after 6 s timeout.
    _gbShowFullscreenPrompt() {
        if (!this._gbOverlay || this._gbOverlay.querySelector('.gb-fs-prompt')) return;
        const prompt = document.createElement('div');
        prompt.className = 'gb-fs-prompt';
        Object.assign(prompt.style, {
            position: 'absolute', inset: '0', zIndex: '9000',
            display: 'flex', flexDirection: 'column',
            alignItems: 'center', justifyContent: 'center',
            background: 'rgba(0,0,0,0.55)', cursor: 'pointer',
            color: '#fff', gap: '16px',
        });
        prompt.innerHTML = `
            <svg width="72" height="72" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" style="pointer-events:none">
                <path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"/>
            </svg>
            <div style="font-size:1.4rem;font-weight:600;letter-spacing:.02em;pointer-events:none">Tap anywhere to enter fullscreen</div>`;

        const dismiss = (doFullscreen) => {
            prompt.remove();
            if (this._gbFsPromptTimer) { clearTimeout(this._gbFsPromptTimer); this._gbFsPromptTimer = null; }
            document.removeEventListener('fullscreenchange', onFsChange);
            if (doFullscreen && !document.fullscreenElement) this._gbToggleFullscreen();
        };
        const onFsChange = () => { if (document.fullscreenElement) dismiss(false); };
        document.addEventListener('fullscreenchange', onFsChange);
        prompt.addEventListener('click', () => dismiss(true));
        prompt.addEventListener('touchend', (e) => { e.preventDefault(); dismiss(true); });
        this._gbOverlay.appendChild(prompt);
        this._gbFsPromptTimer = setTimeout(() => dismiss(false), 6000);
    },

    _gbHideFullscreenPrompt() {
        this._gbOverlay?.querySelector('.gb-fs-prompt')?.remove();
        if (this._gbFsPromptTimer) { clearTimeout(this._gbFsPromptTimer); this._gbFsPromptTimer = null; }
        if (this._gbFsLatchTimer)  { clearTimeout(this._gbFsLatchTimer);  this._gbFsLatchTimer  = null; }
    },

    _gbIsMobile() {
        return window.matchMedia('(pointer: coarse)').matches && window.innerWidth < 900;
    },

    // ─── Go Big Remote — desktop side (command queue polling) ──────────

    _gbStartPolling() {
        this.apiPost('gobig/announce', {}).catch(() => {});
        this._gbPollTimer = setInterval(async () => {
            if (!this._gbActive) return;
            try {
                const data = await this.api('gobig/poll');
                if (!data?.commands?.length) return;
                for (const cmdStr of data.commands) {
                    try { this._gbExecuteRemoteCommand(JSON.parse(cmdStr)); } catch (_) {}
                }
            } catch (_) {}
        }, 600);
    },

    _gbStopPolling() {
        if (this._gbPollTimer) { clearInterval(this._gbPollTimer); this._gbPollTimer = null; }
        fetch('/api/gobig/session', { method: 'DELETE' }).catch(() => {});
    },

    _gbExecuteRemoteCommand(cmd) {
        if (!cmd?.type) return;
        switch (cmd.type) {
            case 'pause': {
                const vid = document.getElementById('gbd-video-player');
                if (vid) { vid.paused ? vid.play().catch(() => {}) : vid.pause(); }
                break;
            }
            case 'css_fullscreen':
                // Remote-triggered: CSS viewport-fill, no browser gesture needed
                this._gbToggleCssFullscreen();
                break;
            case 'fullscreen': {
                if (document.fullscreenElement) {
                    document.exitFullscreen().catch(() => {});
                } else {
                    // Try real browser fullscreen first; fall back to CSS fake-fs if rejected
                    try {
                        document.documentElement.requestFullscreen({ navigationUI: 'hide' })
                            .catch(() => this._gbToggleCssFullscreen());
                    } catch (_) {
                        this._gbToggleCssFullscreen();
                    }
                }
                break;
            }
            case 'fullscreen_enter': {
                // requestFullscreen() cannot succeed inside a poll/setInterval callback
                // (not a trusted user gesture). On same-machine setups the OS keypress
                // path triggers real fullscreen within ~50 ms; we wait 300 ms before
                // showing the latch overlay so it never appears when keypress worked.
                // On cross-device setups (NAS→TV browser) the overlay appears after 300 ms
                // and a tap on the screen provides the trusted gesture.
                if (document.fullscreenElement) break;
                clearTimeout(this._gbFsLatchTimer);
                const fsGuard = () => { clearTimeout(this._gbFsLatchTimer); document.removeEventListener('fullscreenchange', fsGuard); };
                document.addEventListener('fullscreenchange', fsGuard);
                this._gbFsLatchTimer = setTimeout(() => {
                    document.removeEventListener('fullscreenchange', fsGuard);
                    if (!document.fullscreenElement) this._gbShowFullscreenPrompt();
                }, 300);
                break;
            }
            case 'back': {
                const detail = this._gbOverlay?.querySelector('#gb-detail');
                if (detail && detail.style.display === 'block') this._gbBackFromDetail();
                break;
            }
            case 'exit': this.stopGoBigMode(); break;
            case 'play_now': {
                this.stopNightClubMode();
                const { videoId, isMv } = cmd;
                if (isMv) this._gbShowMvDetail(videoId);
                else this._gbShowDetail(videoId);
                setTimeout(() => {
                    const pb = document.getElementById('gbd-play-btn');
                    if (pb && !pb.disabled) pb.click();
                }, 2500);
                break;
            }
            case 'open_series':
                this.stopNightClubMode();
                this._gbShowSeriesDetail(cmd.seriesName, cmd.mediaType || 'tv');
                break;
            case 'play_playlist': {
                const { playlistId } = cmd;
                this.api(`playlists/${playlistId}`).then(data => {
                    if (!data) return;
                    const entries = (data.playlistTracks || []).filter(pt => pt.track);
                    const tracks = entries.map(pt => pt.track);
                    if (tracks.length) this._gbOpenMusicPlayer(0, tracks);
                }).catch(() => {});
                break;
            }
        }
    },

    // ─── Go Big Remote — mobile side (remote control UI) ──────────────

    _gbStartRemoteCheck() {
        if (this._gbRemoteCheckTimer) return;
        this._gbCheckGoBigStatus();
        this._gbRemoteCheckTimer = setInterval(() => this._gbCheckGoBigStatus(), 1500);
    },

    _gbStopRemoteCheck() {
        if (this._gbRemoteCheckTimer) { clearInterval(this._gbRemoteCheckTimer); this._gbRemoteCheckTimer = null; }
    },

    async _gbCheckGoBigStatus() {
        try {
            const data = await this.api('gobig/status');
            if (!data) return; // null = API error / network glitch — leave remote state unchanged
            if (data.active && !this._gbRemoteActive) {
                this._gbHideMobileStartBtn();
                this._gbStartRemote();
            } else if (!data.active && this._gbRemoteActive) {
                this._gbStopRemote();
            } else if (!data.active) {
                this._gbShowMobileStartBtn();
            }
        } catch (_) {}
    },

    // ─── Desktop: listen for mobile Go Big requests ────────────────────────

    _gbStartRequestCheck() {
        if (this._gbRequestCheckTimer || this._gbActive) return;
        this._gbRequestCheckTimer = setInterval(async () => {
            if (this._gbActive) { this._gbStopRequestCheck(); return; }
            try {
                const data = await this.api('gobig/pending');
                if (data?.pending) {
                    this._gbStopRequestCheck();
                    this.startGoBigMode();
                }
            } catch (_) {}
        }, 1000);
    },

    _gbStopRequestCheck() {
        if (this._gbRequestCheckTimer) { clearInterval(this._gbRequestCheckTimer); this._gbRequestCheckTimer = null; }
    },

    // ─── Mobile: "Start Go Big" floating button ────────────────────────────

    _gbShowMobileStartBtn() {
        if (this._gbMobileStartBtn) return;
        const btn = document.createElement('button');
        btn.id = 'gb-mobile-start';
        btn.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:18px;height:18px;flex-shrink:0">
            <rect x="2" y="3" width="20" height="14" rx="2"/><polyline points="8 21 12 17 16 21"/>
        </svg>Go Big`;
        btn.onclick = async () => {
            btn.disabled = true;
            btn.querySelector('svg').outerHTML = '';
            btn.textContent = 'Starting…';
            await fetch('/api/gobig/request', { method: 'POST' }).catch(() => {});
        };
        document.body.appendChild(btn);
        this._gbMobileStartBtn = btn;
    },

    _gbHideMobileStartBtn() {
        if (this._gbMobileStartBtn) { this._gbMobileStartBtn.remove(); this._gbMobileStartBtn = null; }
    },

    async _gbStartRemote() {
        if (this._gbRemoteActive) return;
        this._gbRemoteActive = true;

        const [vidData, mvData, plData, animeData] = await Promise.all([
            this.api('videos?sort=recent&limit=48&grouped=true').catch(() => null),
            this.api('musicvideos?sort=recent&limit=32').catch(() => null),
            this.api('playlists').catch(() => null),
            this.api('videos?mediaType=anime&sort=recent&limit=32&grouped=true').catch(() => null),
        ]);
        const all = vidData?.items || vidData?.videos || [];
        this._gbRemoteVideos = {
            movies:    all.filter(v => v.type !== 'series' && v.mediaType === 'movie'),
            tv:        all.filter(v => v.type === 'series' || v.mediaType === 'tv'),
            docs:      all.filter(v => v.type !== 'series' && v.mediaType === 'documentary'),
            anime:     animeData?.items || animeData?.videos || [],
            mvs:       mvData?.videos || [],
            playlists: Array.isArray(plData) ? plData : [],
        };

        const ol = document.createElement('div');
        ol.id = 'gb-remote-overlay';
        ol.innerHTML = `
            <div id="gbr-header">
                <span id="gbr-logo">Nexus<span style="color:#22c55e">M</span> Remote</span>
                <button id="gbr-close-btn" onclick="App._gbStopRemote()">&#10005;</button>
            </div>
            <div id="gbr-controls">
                <button class="gbr-ctrl-btn" id="gbr-pause-btn" onclick="App._gbRemoteCmd('pause')">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/></svg>
                    <span>Pause</span>
                </button>
                <button class="gbr-ctrl-btn" onclick="App._gbRemoteFullscreen()">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"/></svg>
                    <span>Fullscreen</span>
                </button>
                <button class="gbr-ctrl-btn" onclick="App._gbRemoteCmd('back')">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
                    <span>Back</span>
                </button>
                <button class="gbr-ctrl-btn gbr-ctrl-exit" onclick="App._gbRemoteCmd('exit')">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18.36 6.64a9 9 0 1 1-12.73 0"/><line x1="12" y1="2" x2="12" y2="12"/></svg>
                    <span>Exit TV</span>
                </button>
            </div>
            <div id="gbr-tabs">
                <button class="gbr-tab active" data-tab="movies" onclick="App._gbRemoteSetTab('movies')">Movies</button>
                <button class="gbr-tab" data-tab="tv" onclick="App._gbRemoteSetTab('tv')">TV</button>
                <button class="gbr-tab" data-tab="docs" onclick="App._gbRemoteSetTab('docs')">Docs</button>
                <button class="gbr-tab" data-tab="anime" onclick="App._gbRemoteSetTab('anime')">Anime</button>
                <button class="gbr-tab" data-tab="mvs" onclick="App._gbRemoteSetTab('mvs')">MVs</button>
                <button class="gbr-tab" data-tab="playlists" onclick="App._gbRemoteSetTab('playlists')">&#127925; Lists</button>
            </div>
            <div id="gbr-list"></div>`;
        document.body.appendChild(ol);
        this._gbRemoteOverlay = ol;
        this._gbRemoteTab = 'movies';
        this._gbRenderRemoteList();
    },

    _gbStopRemote() {
        if (!this._gbRemoteActive) return;
        this._gbRemoteActive = false;
        if (this._gbRemoteOverlay) { this._gbRemoteOverlay.remove(); this._gbRemoteOverlay = null; }
        this._gbRemoteVideos = null;
    },

    _gbRenderRemoteList() {
        const listEl = this._gbRemoteOverlay?.querySelector('#gbr-list');
        if (!listEl || !this._gbRemoteVideos) return;
        const tab = this._gbRemoteTab;
        const items = this._gbRemoteVideos[tab] || [];

        // Playlists tab — tap a playlist to launch Nightclub Mode on the desktop
        if (tab === 'playlists') {
            if (!items.length) {
                listEl.innerHTML = '<div class="gbr-empty">No playlists found</div>';
            } else {
                listEl.innerHTML = items.map((pl, idx) => `
                    <div class="gbr-item" onclick="App._gbRemoteTapIdx(${idx})">
                        <div class="gbr-thumb-ph gbr-thumb-pl">&#127925;</div>
                        <div class="gbr-info">
                            <div class="gbr-title">${this.esc(pl.name || 'Playlist')}</div>
                            <div class="gbr-meta">${pl.trackCount || 0} track${pl.trackCount !== 1 ? 's' : ''} &middot; Nightclub Mode</div>
                        </div>
                        <svg class="gbr-play-icon" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
                    </div>`).join('');
            }
            return;
        }

        if (!items.length) { listEl.innerHTML = '<div class="gbr-empty">No content available</div>'; return; }
        const isMv = tab === 'mvs';
        listEl.innerHTML = items.map((v, idx) => {
            let thumb = '';
            if (isMv) {
                thumb = v.thumbnailPath ? `/mvthumb/${v.thumbnailPath}` : '';
            } else {
                thumb = v.posterPath ? `/videometa/${v.posterPath}`
                      : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
            }
            const title = v.seriesName || v.title || 'Unknown';
            const meta  = v.year ? String(v.year) : (v.artist || '');
            return `<div class="gbr-item" onclick="App._gbRemoteTapIdx(${idx})">
                ${thumb
                    ? `<img class="gbr-thumb" src="${thumb}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">`
                    : ''}
                <div class="gbr-thumb-ph" style="${thumb ? 'display:none' : ''}"><svg style="width:28px;height:28px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></div>
                <div class="gbr-info">
                    <div class="gbr-title">${this.esc(title)}</div>
                    ${meta ? `<div class="gbr-meta">${this.esc(meta)}</div>` : ''}
                </div>
                <svg class="gbr-play-icon" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
            </div>`;
        }).join('');
    },

    _gbRemoteSetTab(tab) {
        if (!this._gbRemoteOverlay) return;
        this._gbRemoteTab = tab;
        this._gbRemoteOverlay.querySelectorAll('.gbr-tab').forEach(t =>
            t.classList.toggle('active', t.dataset.tab === tab));
        this._gbRenderRemoteList();
    },

    async _gbRemoteTapIdx(idx) {
        const tab   = this._gbRemoteTab;
        const items = this._gbRemoteVideos?.[tab] || [];
        const v = items[idx];
        if (!v) return;

        // Playlists tab — send play_playlist command
        if (tab === 'playlists') {
            await fetch('/api/gobig/command', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ type: 'play_playlist', playlistId: v.id }),
            }).catch(() => {});
            this._gbRemotePlaying = true;
            this._gbUpdatePauseBtn();
            return;
        }

        const isMv     = tab === 'mvs';
        const isSeries = tab === 'tv' || (tab === 'anime' && v.type === 'series');
        if (isSeries) {
            const seriesName = v.seriesName || v.title || '';
            const seriesMediaType = tab === 'anime' ? 'anime' : 'tv';
            fetch('/api/gobig/command', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ type: 'open_series', seriesName, mediaType: seriesMediaType }),
            }).catch(() => {});
            await this._gbRemoteOpenSeries(seriesName, seriesMediaType);
            return;
        }
        await fetch('/api/gobig/command', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: 'play_now', videoId: v.id, isMv }),
        }).catch(() => {});
        this._gbRemotePlaying = true;
        this._gbUpdatePauseBtn();
    },

    async _gbRemoteCmd(type) {
        await fetch('/api/gobig/command', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type }),
        }).catch(() => {});
        if (type === 'pause') {
            this._gbRemotePlaying = !this._gbRemotePlaying;
            this._gbUpdatePauseBtn();
        }
    },

    // Fullscreen from mobile remote — two-path approach:
    //
    // Path 1 (fire-and-forget): POST /api/gobig/keypress injects a real OS-level 'F' keystroke.
    //   Works when NexusM runs on the SAME machine as the Go Big browser (typical home setup).
    //   The browser receives a trusted keydown → _gbHandleKey → _gbToggleFullscreen() →
    //   requestFullscreen() succeeds. We don't await the result or gate anything on it.
    //
    // Path 2 (gesture latch): always enqueue a 'fullscreen_enter' command.
    //   The poll handler cannot call requestFullscreen() (non-trusted context), so instead
    //   it shows a full-screen tap-target overlay. The user's tap on the screen IS a trusted
    //   gesture — requestFullscreen() called from the click/touchend handler succeeds.
    //   If Path 1 already entered fullscreen, the fullscreenchange event auto-dismisses the
    //   overlay (no-op). Works for both same-machine and server→TV setups.
    async _gbRemoteFullscreen() {
        await fetch('/api/gobig/command', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: 'css_fullscreen' }),
        }).catch(() => {});
    },

    _gbUpdatePauseBtn() {
        const btn = this._gbRemoteOverlay?.querySelector('#gbr-pause-btn');
        if (!btn) return;
        if (this._gbRemotePlaying) {
            btn.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/></svg><span>Pause</span>`;
        } else {
            btn.innerHTML = `<svg viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg><span>Play</span>`;
        }
    },

    async _gbRemoteOpenSeries(seriesName, mediaType = 'tv') {
        this._gbRemoteSeriesView = true;
        this._gbRemoteSeriesName = seriesName;
        const listEl = this._gbRemoteOverlay?.querySelector('#gbr-list');
        if (!listEl) return;

        // Show loading state with back button
        listEl.innerHTML = `
            <div class="gbr-series-header">
                <button class="gbr-back-series" onclick="App._gbRemoteBackToList()">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" style="width:14px;height:14px"><polyline points="15 18 9 12 15 6"/></svg>
                    TV Shows
                </button>
                <span class="gbr-series-title">${this.esc(seriesName)}</span>
            </div>
            <div class="gbr-loading-eps"><div class="gb-loading-dots"><span></span><span></span><span></span></div></div>`;

        try {
            const data = await this.api(`videos?series=${encodeURIComponent(seriesName)}&mediaType=${mediaType}&sort=series&limit=500`);
            // User may have navigated back — bail if so
            if (!this._gbRemoteSeriesView || !this._gbRemoteOverlay) return;

            const eps = data?.videos || [];
            const seasons = {};
            eps.forEach(ep => {
                const s = ep.season || 0;
                if (!seasons[s]) seasons[s] = [];
                seasons[s].push(ep);
            });
            const seasonNums = Object.keys(seasons).map(Number).sort((a, b) => a - b);

            let html = `
                <div class="gbr-series-header">
                    <button class="gbr-back-series" onclick="App._gbRemoteBackToList()">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" style="width:14px;height:14px"><polyline points="15 18 9 12 15 6"/></svg>
                        TV Shows
                    </button>
                    <span class="gbr-series-title">${this.esc(seriesName)}</span>
                </div>`;

            if (!eps.length) {
                html += '<div class="gbr-empty">No episodes found</div>';
            } else {
                seasonNums.forEach(sNum => {
                    const sEps = seasons[sNum];
                    const sLabel = sNum > 0 ? `Season ${sNum}` : 'Specials';
                    html += `<div class="gbr-season-hdr">${sLabel} <span class="gbr-season-count">${sEps.length} ep${sEps.length !== 1 ? 's' : ''}</span></div>`;
                    sEps.forEach(ep => {
                        const thumb = ep.thumbnailPath ? `/videothumb/${ep.thumbnailPath}` : '';
                        const epNum = `S${String(ep.season || 0).padStart(2, '0')}E${String(ep.episode || 0).padStart(2, '0')}`;
                        const dur = this.formatDuration(ep.duration);
                        html += `<div class="gbr-ep-item" onclick="App._gbRemotePlayEp(${ep.id})">
                            ${thumb
                                ? `<img class="gbr-ep-thumb" src="${thumb}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">`
                                : ''}
                            <div class="gbr-ep-thumb-ph" style="${thumb ? 'display:none' : ''}"><svg style="width:28px;height:28px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></div>
                            <div class="gbr-ep-info">
                                <div class="gbr-ep-num">${epNum}</div>
                                <div class="gbr-ep-title">${this.esc(ep.title || 'Episode')}</div>
                                ${dur ? `<div class="gbr-ep-meta">${dur}</div>` : ''}
                            </div>
                            <svg class="gbr-play-icon" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
                        </div>`;
                    });
                });
            }

            const el = this._gbRemoteOverlay?.querySelector('#gbr-list');
            if (el) el.innerHTML = html;
        } catch (_) {
            const el = this._gbRemoteOverlay?.querySelector('#gbr-list');
            if (el) el.innerHTML = `
                <div class="gbr-series-header">
                    <button class="gbr-back-series" onclick="App._gbRemoteBackToList()">← TV Shows</button>
                </div>
                <div class="gbr-empty">Could not load episodes</div>`;
        }
    },

    async _gbRemotePlayEp(epId) {
        await fetch('/api/gobig/command', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: 'play_now', videoId: epId, isMv: false }),
        }).catch(() => {});
        this._gbRemotePlaying = true;
        this._gbUpdatePauseBtn();
    },

    _gbRemoteBackToList() {
        this._gbRemoteSeriesView = false;
        this._gbRemoteSeriesName = null;
        this._gbRenderRemoteList();
    },

    async _gbShowDetail(videoId) {
        const ol = this._gbOverlay;
        if (!ol) return;
        const scroll = ol.querySelector('#gb-scroll');
        const detail = ol.querySelector('#gb-detail');
        if (!detail) return;

        if (scroll) scroll.style.display = 'none';
        detail.style.display = 'block';
        detail.innerHTML = `<div class="gbd-loading"><div class="gb-loading-dots"><span></span><span></span><span></span></div></div>`;

        try {
            const video = await this.api(`videos/${videoId}`);
            if (!video || !this._gbOverlay) return;

            const resLabel = video.height >= 2160 ? '4K' : video.height >= 1080 ? '1080p' : video.height >= 720 ? '720p' : video.height > 0 ? video.height + 'p' : '';
            const hdrLabel = video.hdrFormat || '';

            // Rating + content rating chips
            let ratingChips = '';
            if (video.rating > 0) ratingChips += `<span class="gbd-chip"><svg style="width:11px;height:11px;fill:#f5c518;margin-right:3px;vertical-align:-1px" viewBox="0 0 24 24"><path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/></svg>${video.rating.toFixed(1)}</span>`;
            if (video.imdbRating) ratingChips += `<span class="gbd-chip gbd-chip-imdb">IMDb ${this.esc(video.imdbRating)}</span>`;
            if (video.contentRating) ratingChips += `<span class="gbd-chip">${this.esc(video.contentRating)}</span>`;
            const genreChips = video.genre ? video.genre.split(',').map(g => `<span class="gbd-chip gbd-chip-genre">${this.esc(g.trim())}</span>`).join('') : '';

            // Meta line
            const metaParts = [];
            if (video.year) metaParts.push(video.year);
            if (video.duration) metaParts.push(this.formatDuration(video.duration));
            if (resLabel) metaParts.push(resLabel);
            if (hdrLabel) metaParts.push(hdrLabel);

            // Director / Writer
            let dirHtml = '';
            if (video.directorJson) {
                try {
                    const dirs = JSON.parse(video.directorJson);
                    if (dirs.length) dirHtml += `<div class="gbd-director">Directed by ${dirs.map(d => `<strong>${this.esc(d.name)}</strong>`).join(', ')}</div>`;
                } catch(e) {}
            } else if (video.director) {
                dirHtml += `<div class="gbd-director">Directed by <strong>${this.esc(video.director)}</strong></div>`;
            }
            if (video.writerJson) {
                try {
                    const writers = JSON.parse(video.writerJson);
                    if (writers.length) dirHtml += `<div class="gbd-director">Written by ${writers.map(w => `<strong>${this.esc(w.name)}</strong>`).join(', ')}</div>`;
                } catch(e) {}
            }

            // Cast strip
            let castHtml = '';
            if (video.castJson) {
                try {
                    const cast = JSON.parse(video.castJson).slice(0, 14);
                    if (cast.length) {
                        const items = cast.map(c => {
                            const photo = c.photo ? `/videometa/${c.photo}` : '';
                            const click = c.actorId ? `onclick="App.openActorDetail(${c.actorId})"` : '';
                            return `<div class="gbd-cast-item" ${click} style="${c.actorId ? 'cursor:pointer' : ''}">
                                <div class="gbd-cast-photo">${photo ? `<img src="${photo}" loading="lazy" alt="" onerror="this.style.display='none'">` : `<div class="gbd-cast-ph"></div>`}</div>
                                <div class="gbd-cast-name">${this.esc(c.name)}</div>
                                <div class="gbd-cast-char">${this.esc(c.character || '')}</div>
                            </div>`;
                        }).join('');
                        castHtml = `<div class="gbd-cast-section">
                            <div class="gbd-section-title">Cast</div>
                            <div class="gbd-cast-strip">${items}</div>
                        </div>`;
                    }
                } catch(e) {}
            }

            const backdropUrl = video.backdropPath   ? `/videometa/${this.esc(video.backdropPath)}`
                              : video.thumbnailPath ? `/videothumb/${this.esc(video.thumbnailPath)}` : '';
            const posterUrl   = video.posterPath ? `/videometa/${this.esc(video.posterPath)}` : '';

            detail.innerHTML = `
                <div class="gbd-wrap">
                    ${backdropUrl ? `<div class="gbd-backdrop" style="background-image:url('${backdropUrl}')"><div class="gbd-backdrop-scrim"></div></div>` : ''}
                    <div class="gbd-inner">
                        <div class="gbd-toprow">
                            <button class="gbd-back-btn" onclick="App._gbBackFromDetail()">
                                <svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-chevron-left"/></svg>
                                Back
                            </button>
                        </div>
                        <div class="gbd-hero">
                            ${posterUrl ? `<img class="gbd-poster" src="${posterUrl}" alt="">` : ''}
                            <div class="gbd-meta-col">
                                <h1 class="gbd-title">${this.esc(video.title)}</h1>
                                <div class="gbd-sub">${metaParts.join(' &middot; ')}</div>
                                ${ratingChips || genreChips ? `<div class="gbd-chips">${ratingChips}${genreChips}</div>` : ''}
                                ${video.overview ? `<p class="gbd-overview">${this.esc(video.overview)}</p>` : ''}
                                ${dirHtml}
                                <div class="gbd-actions">
                                    <button class="gbd-play-btn" id="gbd-play-btn">
                                        <svg style="width:22px;height:22px;fill:currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>
                                        Play
                                    </button>
                                </div>
                            </div>
                        </div>
                        ${castHtml}
                        <div class="gbd-player-section" id="gbd-player-section" style="display:none">
                            <button class="gbd-player-back-btn" onclick="App._gbBackFromDetail()">
                                <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-chevron-left"/></svg>
                                Back
                            </button>
                            <video id="gbd-video-player" class="gbd-player" controls></video>
                            <div id="gbd-tc-info" class="gbd-tc-info" style="display:none"></div>
                        </div>
                    </div>
                </div>`;

            detail.scrollTop = 0;
            const playBtn = detail.querySelector('#gbd-play-btn');
            if (playBtn) playBtn.onclick = () => this._gbPlayFromDetail(videoId, video, playBtn);
            // Start focus on Play button (index 1)
            setTimeout(() => { this._gbDetailFocusIdx = 1; this._gbApplyDetailFocus(); }, 0);

        } catch (err) {
            console.error('GB detail error:', err);
            detail.innerHTML = `<div class="gbd-loading"><div style="text-align:center">
                <p style="color:rgba(255,255,255,.4);font-size:14px;margin-bottom:20px">Could not load details.</p>
                <button class="gbd-back-btn" onclick="App._gbBackFromDetail()">← Back</button>
            </div></div>`;
        }
    },

    _gbBackFromDetail() {
        const ol = this._gbOverlay;
        if (!ol) return;
        // Stop any video playing in the detail panel
        const videoEl = document.getElementById('gbd-video-player');
        if (videoEl) { this.stopVideoStream(videoEl); videoEl.pause(); videoEl.src = ''; }
        if (this._gbTranscodeId) {
            const tid = this._gbTranscodeId; this._gbTranscodeId = null;
            this.apiPost(`stop-transcode/${tid}`, {}).catch(() => {});
        }
        this._gbDetailFocusIdx = 0;
        const detail = ol.querySelector('#gb-detail');
        const scroll = ol.querySelector('#gb-scroll');
        if (detail) { detail.style.display = 'none'; detail.innerHTML = ''; }
        if (scroll) scroll.style.display = '';
        // Restore keyboard focus to where it was on the grid
        setTimeout(() => this._gbApplyFocus(), 50);
    },

    async _gbPlayFromDetail(videoId, video, playBtn) {
        if (playBtn) {
            playBtn.disabled = true;
            playBtn.innerHTML = `<svg style="width:18px;height:18px;stroke:currentColor;fill:none;stroke-width:2" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" opacity=".25"/><path d="M12 2a10 10 0 0 1 10 10"/></svg> Loading…`;
        }
        try {
            const streamInfo = await this.api(`stream-info/${videoId}`);
            if (!this._gbOverlay) return;

            if (streamInfo?.transcodeId) this._gbTranscodeId = streamInfo.transcodeId;

            const section = document.getElementById('gbd-player-section');
            const videoEl = document.getElementById('gbd-video-player');
            if (!videoEl || !section) return;

            // Stop any previously playing stream and music before starting video
            this.stopVideoStream(videoEl);
            if (this.audioPlayer) { this.audioPlayer.pause(); this.audioPlayer.src = ''; this.isPlaying = false; }

            section.style.display = 'block';
            section.scrollIntoView({ behavior: 'smooth', block: 'start' });

            const isHLS = streamInfo?.type === 'hls';
            if (isHLS) {
                const hlsUrl = streamInfo.masterPlaylistUrl || streamInfo.playlistUrl;
                if (hlsUrl) {
                    const hevcFallback = streamInfo.mode === 'hevc-passthrough' ? async () => {
                        console.warn('HEVC passthrough failed in Go Big, retrying with SDR transcode');
                        if (this._gbTranscodeId) this.apiPost(`stop-transcode/${this._gbTranscodeId}`, {}).catch(() => {});
                        const fbInfo = await this.api(`stream-info/${videoId}?forceTranscode=true`).catch(e => { console.error('Transcode fallback failed:', e); return null; });
                        const gbEl = document.getElementById('gbd-video-player');
                        if (fbInfo && fbInfo.playlistUrl && gbEl) {
                            if (fbInfo.transcodeId) {
                                this._gbTranscodeId = fbInfo.transcodeId;
                                const tcInfo = document.getElementById('gbd-tc-info');
                                if (tcInfo) { tcInfo.style.display = 'block'; tcInfo.textContent = this.t('misc.transcoding') + (fbInfo.reason ? ' — ' + fbInfo.reason : ''); }
                            }
                            this.playVideoStream(gbEl, fbInfo.playlistUrl);
                        }
                    } : null;
                    this.playVideoStream(videoEl, hlsUrl, hevcFallback);
                }
            } else {
                videoEl.src = `/api/stream-video/${videoId}`;
                videoEl.play();
            }
            this.connectEQToElement(videoEl);

            if (streamInfo?.transcodeId) {
                const tcInfo = document.getElementById('gbd-tc-info');
                if (tcInfo) {
                    const label = streamInfo.mode === 'transcode-cached' ? this.t('misc.cached') : this.t('misc.transcoding');
                    tcInfo.style.display = 'block';
                    tcInfo.textContent = label + (streamInfo.reason ? ' — ' + streamInfo.reason : '');
                }
            }

            // Highlight playing episode row (series view) and clear all others
            document.querySelectorAll('.gbd-ep-row').forEach(row => {
                const playing = row.dataset.epId == videoId;
                row.classList.toggle('gbd-ep-row-playing', playing);
                const np = row.querySelector('.gbd-ep-now-playing');
                if (np) np.style.display = playing ? 'flex' : 'none';
            });

            if (playBtn) {
                playBtn.disabled = false;
                playBtn.innerHTML = `<svg style="width:22px;height:22px;fill:currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg> Now Playing`;
            }
        } catch (err) {
            console.error('GB play error:', err);
            if (playBtn) {
                playBtn.disabled = false;
                playBtn.innerHTML = `<svg style="width:22px;height:22px;fill:currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg> Play`;
            }
        }
    },

    async _gbShowSeriesDetail(seriesName, mediaType = 'tv') {
        const ol = this._gbOverlay;
        if (!ol) return;
        const scroll = ol.querySelector('#gb-scroll');
        const detail = ol.querySelector('#gb-detail');
        if (!detail) return;

        if (scroll) scroll.style.display = 'none';
        detail.style.display = 'block';
        detail.innerHTML = `<div class="gbd-loading"><div class="gb-loading-dots"><span></span><span></span><span></span></div></div>`;

        try {
            const data = await this.api(`videos?series=${encodeURIComponent(seriesName)}&mediaType=${mediaType}&sort=series&limit=500`);
            if (!data?.videos?.length || !this._gbOverlay) return;

            const eps = data.videos;
            const seasons = {};
            let totalDuration = 0;
            let meta = { posterPath:'', backdropPath:'', overview:'', genre:'', rating:0, contentRating:'', castJson:'' };
            eps.forEach(ep => {
                const s = ep.season || 0;
                if (!seasons[s]) seasons[s] = [];
                seasons[s].push(ep);
                totalDuration += ep.duration || 0;
                if (!meta.posterPath   && ep.posterPath)   meta.posterPath   = ep.posterPath;
                if (!meta.backdropPath && ep.backdropPath) meta.backdropPath = ep.backdropPath;
                if (!meta.overview     && ep.overview)     meta.overview     = ep.overview;
                if (!meta.genre        && ep.genre)        meta.genre        = ep.genre;
                if (!meta.rating       && ep.rating)       meta.rating       = ep.rating;
                if (!meta.contentRating && ep.contentRating) meta.contentRating = ep.contentRating;
                if (!meta.castJson     && ep.castJson)     meta.castJson     = ep.castJson;
                if (!meta.thumbnailPath && ep.thumbnailPath) meta.thumbnailPath = ep.thumbnailPath;
            });
            const seasonNums = Object.keys(seasons).map(Number).sort((a, b) => a - b);

            // Chips
            let ratingChips = '';
            if (meta.rating > 0) ratingChips += `<span class="gbd-chip"><svg style="width:11px;height:11px;fill:#f5c518;margin-right:3px;vertical-align:-1px" viewBox="0 0 24 24"><path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/></svg>${meta.rating.toFixed(1)}</span>`;
            if (meta.contentRating) ratingChips += `<span class="gbd-chip">${this.esc(meta.contentRating)}</span>`;
            const genreChips = meta.genre ? meta.genre.split(',').map(g => `<span class="gbd-chip gbd-chip-genre">${this.esc(g.trim())}</span>`).join('') : '';
            const subInfo = `${seasonNums.length} Season${seasonNums.length !== 1 ? 's' : ''} &middot; ${eps.length} Episode${eps.length !== 1 ? 's' : ''} &middot; ${this.formatDuration(totalDuration)}`;

            // Cast
            let castHtml = '';
            if (meta.castJson) {
                try {
                    const cast = JSON.parse(meta.castJson).slice(0, 14);
                    if (cast.length) {
                        const items = cast.map(c => {
                            const photo = c.photo ? `/videometa/${c.photo}` : '';
                            const click = c.actorId ? `onclick="App.openActorDetail(${c.actorId})"` : '';
                            return `<div class="gbd-cast-item" ${click} style="${c.actorId ? 'cursor:pointer' : ''}">
                                <div class="gbd-cast-photo">${photo ? `<img src="${photo}" loading="lazy" alt="" onerror="this.style.display='none'">` : `<div class="gbd-cast-ph"></div>`}</div>
                                <div class="gbd-cast-name">${this.esc(c.name)}</div>
                                <div class="gbd-cast-char">${this.esc(c.character || '')}</div>
                            </div>`;
                        }).join('');
                        castHtml = `<div class="gbd-cast-section">
                            <div class="gbd-section-title">Cast</div>
                            <div class="gbd-cast-strip">${items}</div>
                        </div>`;
                    }
                } catch(e) {}
            }

            // Season / episode rows
            let seasonsHtml = '';
            seasonNums.forEach(sNum => {
                const sEps = seasons[sNum];
                const sLabel = sNum > 0 ? `Season ${sNum}` : 'Specials';
                seasonsHtml += `<div class="gbd-season">
                    <div class="gbd-season-hdr">
                        <span class="gbd-season-title">${sLabel}</span>
                        <span class="gbd-season-count">${sEps.length} episode${sEps.length !== 1 ? 's' : ''}</span>
                    </div>
                    <div class="gbd-ep-list">`;
                sEps.forEach(ep => {
                    const epThumb = ep.thumbnailPath ? `/videothumb/${ep.thumbnailPath}` : '';
                    const epNum = `S${String(ep.season||0).padStart(2,'0')}E${String(ep.episode||0).padStart(2,'0')}`;
                    const badges = [this.formatDuration(ep.duration), ep.height >= 2160 ? '4K' : ep.height >= 1080 ? '1080p' : ep.height >= 720 ? '720p' : '', ep.hdrFormat || ''].filter(Boolean).join(' · ');
                    seasonsHtml += `<div class="gbd-ep-row" data-ep-id="${ep.id}" onclick="App._gbPlayFromDetail(${ep.id},null,null)">
                        <div class="gbd-ep-thumb">
                            ${epThumb ? `<img src="${epThumb}" loading="lazy" alt="" onerror="this.style.display='none'">` : ''}
                            <div class="gbd-ep-thumb-overlay">&#9654;</div>
                        </div>
                        <div class="gbd-ep-info">
                            <div class="gbd-ep-title"><span class="gbd-ep-num">${epNum}</span>${this.esc(ep.title)}</div>
                            ${ep.overview ? `<div class="gbd-ep-desc">${this.esc(ep.overview)}</div>` : ''}
                            <div class="gbd-ep-meta">${badges}</div>
                        </div>
                        <div class="gbd-ep-now-playing" style="display:none">&#9654; Now Playing</div>
                    </div>`;
                });
                seasonsHtml += '</div></div>';
            });

            const backdropUrl = meta.backdropPath   ? `/videometa/${this.esc(meta.backdropPath)}`
                              : meta.thumbnailPath ? `/videothumb/${this.esc(meta.thumbnailPath)}` : '';
            const posterUrl   = meta.posterPath ? `/videometa/${this.esc(meta.posterPath)}` : '';

            detail.innerHTML = `
                <div class="gbd-wrap">
                    ${backdropUrl ? `<div class="gbd-backdrop" style="background-image:url('${backdropUrl}')"><div class="gbd-backdrop-scrim"></div></div>` : ''}
                    <div class="gbd-inner">
                        <div class="gbd-toprow">
                            <button class="gbd-back-btn" onclick="App._gbBackFromDetail()">
                                <svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-chevron-left"/></svg>
                                Back
                            </button>
                        </div>
                        <div class="gbd-hero">
                            ${posterUrl ? `<img class="gbd-poster" src="${posterUrl}" alt="">` : ''}
                            <div class="gbd-meta-col">
                                <h1 class="gbd-title">${this.esc(seriesName)}</h1>
                                <div class="gbd-sub">${subInfo}</div>
                                ${ratingChips || genreChips ? `<div class="gbd-chips">${ratingChips}${genreChips}</div>` : ''}
                                ${meta.overview ? `<p class="gbd-overview">${this.esc(meta.overview)}</p>` : ''}
                            </div>
                        </div>
                        ${castHtml}
                        <div class="gbd-player-section" id="gbd-player-section" style="display:none">
                            <video id="gbd-video-player" class="gbd-player" controls></video>
                            <div id="gbd-tc-info" class="gbd-tc-info" style="display:none"></div>
                        </div>
                        <div class="gbd-seasons">${seasonsHtml}</div>
                    </div>
                </div>`;
            detail.scrollTop = 0;
            // Start focus on first episode (index 1, after Back button)
            setTimeout(() => { this._gbDetailFocusIdx = 1; this._gbApplyDetailFocus(); }, 0);

        } catch (err) {
            console.error('GB series detail error:', err);
            detail.innerHTML = `<div class="gbd-loading"><div style="text-align:center">
                <p style="color:rgba(255,255,255,.4);font-size:14px;margin-bottom:20px">Could not load series.</p>
                <button class="gbd-back-btn" onclick="App._gbBackFromDetail()">← Back</button>
            </div></div>`;
        }
    },

    _gbOpenMovie(idx) {
        const v = this._gbMovies?.[idx];
        if (!v) return;
        this._gbShowDetail(v.id);
    },

    _gbOpenDoco(idx) {
        const v = this._gbDocos?.[idx];
        if (!v) return;
        this._gbShowDetail(v.id);
    },

    _gbOpenTv(idx) {
        const v = this._gbTv?.[idx];
        if (!v) return;
        if (v.seriesName) this._gbShowSeriesDetail(v.seriesName, 'tv');
        else this._gbShowDetail(v.id);
    },

    _gbOpenAnime(idx) {
        const v = this._gbAnime?.[idx];
        if (!v) return;
        if (v.seriesName) this._gbShowSeriesDetail(v.seriesName, 'anime');
        else this._gbShowDetail(v.id);
    },

    _gbOpenMv(idx) {
        const v = this._gbMvs?.[idx];
        if (!v) return;
        this._gbShowMvDetail(v.id);
    },

    async _gbShowMvDetail(id) {
        const ol = this._gbOverlay;
        if (!ol) return;
        const scroll = ol.querySelector('#gb-scroll');
        const detail = ol.querySelector('#gb-detail');
        if (!detail) return;

        if (scroll) scroll.style.display = 'none';
        detail.style.display = 'block';
        detail.innerHTML = `<div class="gbd-loading"><div class="gb-loading-dots"><span></span><span></span><span></span></div></div>`;

        try {
            const video = await this.api(`musicvideos/${id}`);
            if (!video || !this._gbOverlay) return;

            const resLabel = video.height >= 2160 ? '4K' : video.height >= 1080 ? '1080p' : video.height >= 720 ? '720p' : '';
            const metaParts = [video.artist, video.year, this.formatDuration(video.duration), resLabel].filter(Boolean);
            const thumbUrl = video.thumbnailPath ? `/mvthumb/${this.esc(video.thumbnailPath)}` : '';
            const genreChips = video.genre ? video.genre.split(',').map(g => `<span class="gbd-chip gbd-chip-genre">${this.esc(g.trim())}</span>`).join('') : '';

            detail.innerHTML = `
                <div class="gbd-wrap">
                    ${thumbUrl ? `<div class="gbd-backdrop gbd-backdrop-mv" style="background-image:url('${thumbUrl}')"><div class="gbd-backdrop-scrim"></div></div>` : ''}
                    <div class="gbd-inner">
                        <div class="gbd-toprow">
                            <button class="gbd-back-btn" onclick="App._gbBackFromDetail()">
                                <svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-chevron-left"/></svg>
                                Back
                            </button>
                        </div>
                        <div class="gbd-hero">
                            ${thumbUrl ? `<img class="gbd-mv-thumb" src="${thumbUrl}" alt="">` : ''}
                            <div class="gbd-meta-col">
                                <div class="gbd-mv-label">Music Video</div>
                                <h1 class="gbd-title">${this.esc(video.title)}</h1>
                                <div class="gbd-sub">${metaParts.join(' &middot; ')}</div>
                                ${genreChips ? `<div class="gbd-chips">${genreChips}</div>` : ''}
                                ${video.album ? `<div class="gbd-director">Album: <strong>${this.esc(video.album)}</strong></div>` : ''}
                                <div class="gbd-actions">
                                    <button class="gbd-play-btn" id="gbd-play-btn">
                                        <svg style="width:22px;height:22px;fill:currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>
                                        Play
                                    </button>
                                </div>
                            </div>
                        </div>
                        <div class="gbd-player-section" id="gbd-player-section" style="display:none">
                            <video id="gbd-video-player" class="gbd-player" controls></video>
                        </div>
                    </div>
                </div>`;

            detail.scrollTop = 0;
            const playBtn = detail.querySelector('#gbd-play-btn');
            if (playBtn) playBtn.onclick = () => {
                const section = document.getElementById('gbd-player-section');
                const videoEl = document.getElementById('gbd-video-player');
                if (!section || !videoEl) return;
                playBtn.disabled = true;
                playBtn.innerHTML = `<svg style="width:22px;height:22px;fill:currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg> Now Playing`;
                videoEl.src = `/api/stream-musicvideo/${id}`;
                section.style.display = 'block';
                section.scrollIntoView({ behavior: 'smooth', block: 'start' });
                videoEl.play();
                this.connectEQToElement(videoEl);
            };
            // Start focus on Play button (index 1)
            setTimeout(() => { this._gbDetailFocusIdx = 1; this._gbApplyDetailFocus(); }, 0);

        } catch (err) {
            console.error('GB MV detail error:', err);
            detail.innerHTML = `<div class="gbd-loading"><div style="text-align:center">
                <p style="color:rgba(255,255,255,.4);font-size:14px;margin-bottom:20px">Could not load video.</p>
                <button class="gbd-back-btn" onclick="App._gbBackFromDetail()">← Back</button>
            </div></div>`;
        }
    },

    // ─── Go Big Search ───────────────────────────────────────────────────────

    _gbGetSearchItems() {
        const res = this._gbOverlay?.querySelector('#gb-search-results');
        if (!res) return [];
        return Array.from(res.querySelectorAll('[tabindex="0"]'));
    },

    _gbApplySearchFocus(items) {
        const el = items[this._gbSearchFocusIdx];
        if (!el) return;
        el.focus({ preventScroll: true });
        el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    },

    _gbOpenSearch() {
        const ol = this._gbOverlay;
        if (!ol) return;
        const searchEl = ol.querySelector('#gb-search');
        if (!searchEl) return;
        searchEl.style.display = 'flex';
        this._gbSearchActive = true;
        this._gbSearchFocusIdx = -1;
        const input = ol.querySelector('#gb-search-input');
        if (input) {
            input.value = '';
            if (!input._gbBound) {
                input._gbBound = true;
                let _t;
                input.addEventListener('input', () => {
                    clearTimeout(_t);
                    const q = input.value.trim();
                    const res = ol.querySelector('#gb-search-results');
                    if (q.length < 2) {
                        if (res) res.innerHTML = '<div class="gbs-empty">Type at least 2 characters...</div>';
                        return;
                    }
                    _t = setTimeout(() => this._gbDoSearch(q), 350);
                });
                input.addEventListener('keydown', ev => {
                    if (ev.key === 'Enter') { clearTimeout(_t); const q = input.value.trim(); if (q.length >= 2) this._gbDoSearch(q); }
                });
            }
            setTimeout(() => input.focus(), 30);
        }
        const res = ol.querySelector('#gb-search-results');
        if (res) res.innerHTML = '<div class="gbs-empty">Start typing to search...</div>';
    },

    _gbCloseSearch() {
        const ol = this._gbOverlay;
        if (!ol) return;
        const searchEl = ol.querySelector('#gb-search');
        if (searchEl) searchEl.style.display = 'none';
        this._gbSearchActive = false;
        this._gbSearchVids = [];
        this._gbSearchMvs = [];
        this._gbSearchTracks = [];
        this._gbSearchArtists = [];
        this._gbSearchActors = [];
        setTimeout(() => this._gbApplyFocus(), 50);
    },

    async _gbDoSearch(q) {
        const ol = this._gbOverlay;
        if (!ol || !this._gbSearchActive) return;
        const res = ol.querySelector('#gb-search-results');
        if (!res) return;
        res.innerHTML = '<div class="gbs-searching"><div class="gb-loading-dots"><span></span><span></span><span></span></div></div>';
        const data = await this.api(`search?q=${encodeURIComponent(q)}`);
        if (!data || !this._gbSearchActive) return;
        this._gbRenderSearchResults(data, res);
    },

    _gbRenderSearchResults(data, container) {
        this._gbSearchFocusIdx = -1;
        this._gbSearchVids = [];
        this._gbSearchMvs = data.musicVideos || [];
        this._gbSearchTracks = data.tracks || [];
        this._gbSearchArtists = data.artists || [];
        this._gbSearchActors = data.actors || [];

        let html = '';
        let total = 0;

        // Videos (movies, TV, anime, docs) — deduplicate series
        const seen = new Set();
        this._gbSearchVids = (data.videos || []).filter(v => {
            if ((v.mediaType === 'tv' || v.mediaType === 'anime') && v.seriesName) {
                if (seen.has(v.seriesName)) return false;
                seen.add(v.seriesName);
            }
            return true;
        });

        if (this._gbSearchVids.length) {
            total += this._gbSearchVids.length;
            html += `<div class="gbs-section"><div class="gbs-section-title">Movies &amp; Shows &mdash; ${this._gbSearchVids.length}</div><div class="gbs-grid">`;
            this._gbSearchVids.forEach((v, i) => {
                const poster = v.posterPath ? `/videometa/${v.posterPath}` : v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '';
                const label = this.esc(v.seriesName || v.title);
                const sub = v.year ? String(v.year) : '';
                html += `<div class="gbs-card-video" tabindex="0" onclick="App._gbSearchOpenVid(${i})">
                    <div class="gbs-poster">${poster ? `<img src="${poster}" loading="lazy" alt="" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'"><div class="gbs-poster-placeholder" style="display:none"><svg style="width:32px;height:32px;stroke:currentColor;fill:none;stroke-width:1.5;opacity:.4"><use href="#icon-film"/></svg></div>` : `<div class="gbs-poster-placeholder"><svg style="width:32px;height:32px;stroke:currentColor;fill:none;stroke-width:1.5;opacity:.4"><use href="#icon-film"/></svg></div>`}</div>
                    <div class="gbs-label">${label}</div>
                    ${sub ? `<div class="gbs-sublabel">${sub}</div>` : ''}
                </div>`;
            });
            html += '</div></div>';
        }

        if (this._gbSearchMvs.length) {
            total += this._gbSearchMvs.length;
            html += `<div class="gbs-section"><div class="gbs-section-title">Music Videos &mdash; ${this._gbSearchMvs.length}</div><div class="gbs-grid">`;
            this._gbSearchMvs.forEach((v, i) => {
                const thumb = v.thumbnailPath ? `/mvthumb/${v.thumbnailPath}` : '';
                html += `<div class="gbs-card-mv" tabindex="0" onclick="App._gbSearchOpenMv(${i})">
                    <div class="gbs-thumb">${thumb ? `<img src="${thumb}" loading="lazy" alt="">` : `<div class="gbs-thumb-placeholder"><svg style="width:28px;height:28px;stroke:currentColor;fill:none;stroke-width:1.5;opacity:.4"><use href="#icon-video"/></svg></div>`}</div>
                    <div class="gbs-label">${this.esc(v.title)}</div>
                    <div class="gbs-sublabel">${this.esc(v.artist || '')}</div>
                </div>`;
            });
            html += '</div></div>';
        }

        if (this._gbSearchTracks.length) {
            total += this._gbSearchTracks.length;
            html += `<div class="gbs-section"><div class="gbs-section-title">Tracks &mdash; ${this._gbSearchTracks.length}</div>`;
            this._gbSearchTracks.forEach((t, i) => {
                html += `<div class="gbs-track-row" tabindex="0" onclick="App._gbSearchPlayTrack(${i})">
                    <div class="gbs-track-art-placeholder"><svg style="width:20px;height:20px;stroke:currentColor;fill:none;stroke-width:1.5;opacity:.5"><use href="#icon-music"/></svg></div>
                    <div style="flex:1;min-width:0">
                        <div class="gbs-track-title">${this.esc(t.title)}</div>
                        <div class="gbs-track-sub">${this.esc(t.artist || '')}${t.album ? ' &middot; ' + this.esc(t.album) : ''}</div>
                    </div>
                    <span class="gbs-track-dur">${this.formatDuration(t.duration)}</span>
                </div>`;
            });
            html += '</div>';
        }

        if (this._gbSearchArtists.length) {
            total += this._gbSearchArtists.length;
            html += `<div class="gbs-section"><div class="gbs-section-title">Artists &mdash; ${this._gbSearchArtists.length}</div><div class="gbs-grid">`;
            this._gbSearchArtists.forEach((a, i) => {
                const img = a.imagePath ? `/singerphoto/${a.imagePath}` : '';
                html += `<div class="gbs-card-person" tabindex="0" onclick="App._gbSearchOpenArtist(${i})">
                    <div class="gbs-person-img">${img ? `<img src="${img}" loading="lazy" alt="">` : `<div class="gbs-person-placeholder"><svg style="width:28px;height:28px;stroke:currentColor;fill:none;stroke-width:1.5;opacity:.4"><use href="#icon-mic"/></svg></div>`}</div>
                    <div class="gbs-label">${this.esc(a.name)}</div>
                    <div class="gbs-sublabel">${a.trackCount || 0} tracks</div>
                </div>`;
            });
            html += '</div></div>';
        }

        if (this._gbSearchActors.length) {
            total += this._gbSearchActors.length;
            html += `<div class="gbs-section"><div class="gbs-section-title">Actors &mdash; ${this._gbSearchActors.length}</div><div class="gbs-grid">`;
            this._gbSearchActors.forEach((a, i) => {
                const img = a.imageCached ? `/actorphoto/${a.imageCached}` : '';
                html += `<div class="gbs-card-person" tabindex="0" onclick="App._gbSearchOpenActor(${i})">
                    <div class="gbs-person-img">${img ? `<img src="${img}" loading="lazy" alt="">` : `<div class="gbs-person-placeholder"><svg style="width:28px;height:28px;stroke:currentColor;fill:none;stroke-width:1.5;opacity:.4"><use href="#icon-users"/></svg></div>`}</div>
                    <div class="gbs-label">${this.esc(a.name)}</div>
                </div>`;
            });
            html += '</div></div>';
        }

        container.innerHTML = total === 0
            ? '<div class="gbs-empty">No results found</div>'
            : html;
    },

    _gbSearchOpenVid(idx) {
        const v = this._gbSearchVids?.[idx];
        if (!v) return;
        this._gbCloseSearch();
        if ((v.mediaType === 'tv' || v.mediaType === 'anime') && v.seriesName)
            this._gbShowSeriesDetail(v.seriesName, v.mediaType);
        else
            this._gbShowDetail(v.id);
    },

    _gbSearchOpenMv(idx) {
        const v = this._gbSearchMvs?.[idx];
        if (!v) return;
        this._gbCloseSearch();
        this._gbShowMvDetail(v.id);
    },

    _gbSearchPlayTrack(idx) {
        if (!this._gbSearchTracks?.length) return;
        this._gbOpenMusicPlayer(idx, this._gbSearchTracks);
    },

    async _gbSearchOpenArtist(idx) {
        const a = this._gbSearchArtists?.[idx];
        if (!a) return;
        // Re-search with artist name so user sees all their content (tracks, albums, MVs)
        const input = this._gbOverlay?.querySelector('#gb-search-input');
        if (input) input.value = a.name;
        await this._gbDoSearch(a.name);
    },

    async _gbSearchOpenActor(idx) {
        const a = this._gbSearchActors?.[idx];
        if (!a) return;
        const movies = await this.api(`actors/${a.id}/movies`);
        if (!movies?.length) return;
        this._gbCloseSearch();
        const v = movies[0];
        if ((v.mediaType === 'tv' || v.mediaType === 'anime') && v.seriesName)
            this._gbShowSeriesDetail(v.seriesName, v.mediaType);
        else
            this._gbShowDetail(v.id);
    },

    // ─── Go Big Music Player ─────────────────────────────────────────────────

    _gbOpenMusicPlayer(idx, tracks) {
        const ol = this._gbOverlay;
        if (!ol) return;
        this._gbMusicTracks = tracks || [];
        this._gbMusicIdx = idx;
        this._gbMusicShuffle = false;
        this._gbMusicRepeat = 'off';
        // Hide search, show music player
        const searchEl = ol.querySelector('#gb-search');
        if (searchEl) searchEl.style.display = 'none';
        this._gbSearchActive = false;
        const panel = ol.querySelector('#gb-music-player');
        if (!panel) return;
        panel.style.display = 'flex';
        // Hide topbar Search + Exit while music player is open
        const gbSearchBtn = ol.querySelector('#gb-search-btn');
        const gbTopExit  = ol.querySelector('#gb-top-exit');
        if (gbSearchBtn) gbSearchBtn.style.display = 'none';
        if (gbTopExit)  gbTopExit.style.display  = 'none';
        // Reset button states
        ol.querySelector('#gbm-shuffle-btn')?.classList.remove('gbm-btn-active');
        ol.querySelector('#gbm-repeat-btn')?.classList.remove('gbm-btn-active');
        const badge = ol.querySelector('#gbm-repeat-one-badge');
        if (badge) badge.style.display = 'none';
        this._gbMusicLoadTrack();
    },

    _gbMusicLoadTrack() {
        const ol = this._gbOverlay;
        if (!ol) return;
        const tracks = this._gbMusicTracks;
        const idx = this._gbMusicIdx;
        if (!tracks?.length || idx < 0 || idx >= tracks.length) return;
        const track = tracks[idx];

        // UI elements
        const artEl  = ol.querySelector('#gbm-art');
        const artPh  = ol.querySelector('#gbm-art-ph');
        const bgEl   = ol.querySelector('#gbm-bg');
        const titleEl = ol.querySelector('#gbm-title');
        const metaEl  = ol.querySelector('#gbm-meta');
        const nextEl  = ol.querySelector('#gbm-next-info');
        const audio   = ol.querySelector('#gbm-audio');
        if (!audio) return;

        // Album art + blurred background
        const artUrl = `/api/cover/track/${track.id}`;
        if (artEl) {
            artEl.style.display = 'none';
            artEl.src = artUrl;
            artEl.onload  = () => { artEl.style.display = 'block'; if (artPh) artPh.style.display = 'none'; };
            artEl.onerror = () => { artEl.style.display = 'none';  if (artPh) artPh.style.display = 'flex'; };
        }
        if (bgEl) bgEl.style.backgroundImage = `url('${artUrl}')`;

        // Track info
        if (titleEl) titleEl.textContent = track.title || '';
        const metaParts = [track.artist, track.album].filter(Boolean);
        if (metaEl) metaEl.textContent = metaParts.join(' · ');

        // Next track label
        let nextEl_text = '';
        if (this._gbMusicRepeat === 'one') {
            nextEl_text = `Repeating: ${track.title}`;
        } else if (this._gbMusicShuffle) {
            nextEl_text = 'Next: (shuffle)';
        } else {
            const next = tracks[idx + 1] || (this._gbMusicRepeat === 'all' ? tracks[0] : null);
            if (next) nextEl_text = `Next: ${next.title}${next.artist ? ' \u2014 ' + next.artist : ''}`;
        }
        if (nextEl) nextEl.textContent = nextEl_text;

        // Reset progress display
        this._gbMusicSetProgress(0, 0);

        // Stop previous, start new stream
        clearInterval(this._gbMusicProgressInterval);
        audio.pause();
        audio.src = `/api/stream/${track.id}`;
        audio.play().catch(() => {});
        this._gbMusicUpdatePlayBtn(true);

        // Progress polling
        this._gbMusicProgressInterval = setInterval(() => {
            if (!this._gbOverlay) { clearInterval(this._gbMusicProgressInterval); return; }
            const a = this._gbOverlay.querySelector('#gbm-audio');
            if (a && isFinite(a.duration) && a.duration > 0)
                this._gbMusicSetProgress(a.currentTime, a.duration);
        }, 500);

        audio.onended = () => this._gbMusicPlayerNext();
        audio.onpause = () => this._gbMusicUpdatePlayBtn(false);
        audio.onplay  = () => this._gbMusicUpdatePlayBtn(true);
    },

    _gbMusicSetProgress(cur, dur) {
        const ol = this._gbOverlay;
        if (!ol) return;
        const pct = dur > 0 ? (cur / dur) * 100 : 0;
        const fill  = ol.querySelector('#gbm-fill');
        const thumb = ol.querySelector('#gbm-thumb');
        const curEl = ol.querySelector('#gbm-cur');
        const durEl = ol.querySelector('#gbm-dur');
        if (fill)  fill.style.width = pct + '%';
        if (thumb) thumb.style.left = pct + '%';
        if (curEl) curEl.textContent = this.formatDuration(Math.floor(cur));
        if (durEl) durEl.textContent = this.formatDuration(Math.floor(dur));
    },

    _gbMusicUpdatePlayBtn(playing) {
        const icon = this._gbOverlay?.querySelector('#gbm-play-icon');
        if (!icon) return;
        icon.innerHTML = playing
            ? '<path d="M6 5h4v14H6zM14 5h4v14h-4z"/>'
            : '<path d="M8 5v14l11-7z"/>';
    },

    _gbMusicPlayerToggle() {
        const audio = this._gbOverlay?.querySelector('#gbm-audio');
        if (!audio) return;
        audio.paused ? audio.play().catch(() => {}) : audio.pause();
    },

    _gbMusicPlayerPrev() {
        if (!this._gbMusicTracks?.length) return;
        if (this._gbMusicIdx > 0) { this._gbMusicIdx--; this._gbMusicLoadTrack(); }
    },

    _gbMusicPlayerNext() {
        if (!this._gbMusicTracks?.length) return;
        if (this._gbMusicRepeat === 'one') { this._gbMusicLoadTrack(); return; }
        let nextIdx;
        if (this._gbMusicShuffle) {
            nextIdx = Math.floor(Math.random() * this._gbMusicTracks.length);
        } else {
            nextIdx = this._gbMusicIdx + 1;
        }
        if (nextIdx < this._gbMusicTracks.length) {
            this._gbMusicIdx = nextIdx;
            this._gbMusicLoadTrack();
        } else if (this._gbMusicRepeat === 'all') {
            this._gbMusicIdx = 0;
            this._gbMusicLoadTrack();
        } else {
            const audio = this._gbOverlay?.querySelector('#gbm-audio');
            if (audio) { audio.pause(); }
            this._gbMusicUpdatePlayBtn(false);
        }
    },

    _gbMusicPlayerSeek(event) {
        const audio = this._gbOverlay?.querySelector('#gbm-audio');
        if (!audio || !isFinite(audio.duration) || audio.duration <= 0) return;
        const rect = event.currentTarget.getBoundingClientRect();
        const pct  = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
        audio.currentTime = pct * audio.duration;
    },

    _gbMusicPlayerClose() {
        clearInterval(this._gbMusicProgressInterval);
        this._gbMusicProgressInterval = null;
        const ol = this._gbOverlay;
        if (!ol) return;
        const audio = ol.querySelector('#gbm-audio');
        if (audio) { audio.pause(); audio.src = ''; audio.load(); }
        ol.querySelector('#gb-music-player').style.display = 'none';
        // Restore topbar Search + Exit buttons
        const gbSearchBtn = ol.querySelector('#gb-search-btn');
        const gbTopExit  = ol.querySelector('#gb-top-exit');
        if (gbSearchBtn) gbSearchBtn.style.display = '';
        if (gbTopExit)  gbTopExit.style.display  = '';
        this._gbMusicTracks = [];
        this._gbMusicIdx = -1;
        setTimeout(() => this._gbApplyFocus(), 50);
    },

    _gbMusicToggleShuffle() {
        this._gbMusicShuffle = !this._gbMusicShuffle;
        const btn = this._gbOverlay?.querySelector('#gbm-shuffle-btn');
        if (btn) btn.classList.toggle('gbm-btn-active', this._gbMusicShuffle);
        // Refresh next-track label
        this._gbMusicLoadNextLabel();
    },

    _gbMusicToggleRepeat() {
        const modes = ['off', 'all', 'one'];
        const i = modes.indexOf(this._gbMusicRepeat || 'off');
        this._gbMusicRepeat = modes[(i + 1) % 3];
        const ol = this._gbOverlay;
        if (!ol) return;
        const btn = ol.querySelector('#gbm-repeat-btn');
        const badge = ol.querySelector('#gbm-repeat-one-badge');
        const active = this._gbMusicRepeat !== 'off';
        if (btn) btn.classList.toggle('gbm-btn-active', active);
        if (badge) badge.style.display = this._gbMusicRepeat === 'one' ? 'block' : 'none';
        this._gbMusicLoadNextLabel();
    },

    _gbMusicLoadNextLabel() {
        const ol = this._gbOverlay;
        const nextEl = ol?.querySelector('#gbm-next-info');
        if (!nextEl || !this._gbMusicTracks?.length) return;
        const idx = this._gbMusicIdx;
        const tracks = this._gbMusicTracks;
        const track = tracks[idx];
        if (!track) return;
        let text = '';
        if (this._gbMusicRepeat === 'one') {
            text = `Repeating: ${track.title}`;
        } else if (this._gbMusicShuffle) {
            text = 'Next: (shuffle)';
        } else {
            const next = tracks[idx + 1] || (this._gbMusicRepeat === 'all' ? tracks[0] : null);
            if (next) text = `Next: ${next.title}${next.artist ? ' \u2014 ' + next.artist : ''}`;
        }
        nextEl.textContent = text;
    },

    // ─── Night Club Mode ──────────────────────────────────────────────────────

    startNightClubMode() {
        if (this._ncActive) return;

        // Stop any Go Big video playing in the background
        const gbVid = document.getElementById('gbd-video-player');
        if (gbVid) { this.stopVideoStream(gbVid); gbVid.pause(); gbVid.src = ''; }

        // Shuffle-play the current playlist
        if (this._playlistTracks && this._playlistTracks.length > 0) {
            this.playPlaylistShuffle();
            this.shuffle = true;
            const sb = document.getElementById('btn-shuffle');
            if (sb) sb.style.color = 'var(--accent)';
        }

        this._ncActive = true;
        document.body.classList.add('nc-active');

        // Build overlay DOM
        const ol = document.createElement('div');
        ol.id = 'nc-overlay';
        ol.innerHTML = `
            <div id="nc-label">Night Club Mode</div>
            <div id="nc-hint">Press ESC or double-click to exit</div>
            <div class="nc-beams">
                <div class="nc-beam nc-beam-1"></div>
                <div class="nc-beam nc-beam-2"></div>
                <div class="nc-beam nc-beam-3"></div>
            </div>
            <div id="nc-floor"></div>
            <div id="nc-particles"></div>
            <div id="nc-content">
                <div id="nc-art-wrap">
                    <div id="nc-art-glow"></div>
                    <img id="nc-art" alt="">
                    <div id="nc-art-placeholder">&#9835;</div>
                    <img id="nc-art-reflect" alt="">
                </div>
                <div id="nc-info">
                    <div id="nc-title">&mdash;</div>
                    <div id="nc-artist"></div>
                </div>
            </div>
            <div id="nc-dancer-wrap"><pre id="nc-dancer"></pre></div>
            <canvas id="nc-canvas"></canvas>
            <div id="nc-viz-dots">
                <button class="nc-viz-dot" id="nc-dot-0" onclick="App._ncSetViz(0)" title="Spectrum"></button>
                <button class="nc-viz-dot" id="nc-dot-1" onclick="App._ncSetViz(1)" title="Pulse"></button>
                <button class="nc-viz-dot" id="nc-dot-2" onclick="App._ncSetViz(2)" title="Neon 3D"></button>
            </div>
            <div id="nc-viz-label"></div>
            <div id="nc-controls">
                <button class="nc-ctrl-btn" id="nc-btn-prev" title="Previous">&#9664;&#9664;</button>
                <button class="nc-ctrl-btn" id="nc-btn-play" title="Play / Pause">&#9646;&#9646;</button>
                <button class="nc-ctrl-btn" id="nc-btn-next" title="Next">&#9654;&#9654;</button>
                <div class="nc-ctrl-sep"></div>
                <button class="nc-ctrl-btn" id="nc-btn-eq" title="Equalizer">EQ</button>
                <button class="nc-ctrl-btn" id="nc-btn-exit" title="Exit (ESC)">&#10005;</button>
            </div>`;
        document.body.appendChild(ol);
        this._ncOverlay = ol;

        // Wire controls
        ol.querySelector('#nc-btn-prev').onclick = () => document.getElementById('btn-prev')?.click();
        ol.querySelector('#nc-btn-play').onclick = () => this.togglePlay();
        ol.querySelector('#nc-btn-next').onclick = () => document.getElementById('btn-next')?.click();
        ol.querySelector('#nc-btn-eq').onclick   = () => document.getElementById('btn-eq')?.click();
        ol.querySelector('#nc-btn-exit').onclick = () => this.stopNightClubMode();
        ol.addEventListener('dblclick', (e) => { if (!e.target.closest('.nc-ctrl-btn')) this.stopNightClubMode(); });

        // ESC + F keys
        this._ncKeyHandler = (e) => {
            if (e.key === 'Escape') { this.stopNightClubMode(); }
            else if (e.key === 'f' || e.key === 'F') {
                e.preventDefault();
                if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
                else ol.requestFullscreen({ navigationUI: 'hide' }).catch(() => {});
            }
        };
        document.addEventListener('keydown', this._ncKeyHandler);

        // Sync play button to current state
        const ncPlay = ol.querySelector('#nc-btn-play');
        if (ncPlay) ncPlay.textContent = this.isPlaying ? '\u23F8' : '\u25B6';

        this._ncUpdateSong();
        this._ncSpawnParticles();
        this._ncDancerFrame = 0;
        this._ncStartDancer();
        this._ncSetupCanvas();
        this._ncSyncVizDots();
        this._ncDrawLoop();
    },

    stopNightClubMode() {
        if (!this._ncActive) return;
        this._ncActive = false;
        document.body.classList.remove('nc-active');
        if (this._ncOverlay)       { this._ncOverlay.remove(); this._ncOverlay = null; }
        if (this._ncAnimFrame)     { cancelAnimationFrame(this._ncAnimFrame); this._ncAnimFrame = null; }
        if (this._ncDancerTimer)   { clearInterval(this._ncDancerTimer); this._ncDancerTimer = null; }
        if (this._ncKeyHandler)    { document.removeEventListener('keydown', this._ncKeyHandler); this._ncKeyHandler = null; }
        if (this._ncResizeHandler) { window.removeEventListener('resize', this._ncResizeHandler); this._ncResizeHandler = null; }
        this._ncCanvas = null;
        this._ncCtx = null;
    },

    _ncUpdateSong() {
        if (!this._ncActive || !this._ncOverlay) return;
        const track  = this.currentTrack;
        const title  = track?.title  || document.getElementById('player-title')?.textContent  || '';
        const artist = track?.artist || document.getElementById('player-artist')?.textContent || '';
        const ncTitle = this._ncOverlay.querySelector('#nc-title');
        const ncArtist = this._ncOverlay.querySelector('#nc-artist');
        const ncArt    = this._ncOverlay.querySelector('#nc-art');
        const ncRef    = this._ncOverlay.querySelector('#nc-art-reflect');
        const ncPh     = this._ncOverlay.querySelector('#nc-art-placeholder');
        if (ncTitle)  ncTitle.textContent  = title  || '\u2014';
        if (ncArtist) ncArtist.textContent = artist || '';
        const id = track?.id;
        if (id) {
            const src = `/api/cover/track/${id}`;
            if (ncArt) {
                ncArt.onload = () => {
                    ncArt.style.display = 'block';
                    if (ncRef) { ncRef.src = src; ncRef.style.display = 'block'; }
                    if (ncPh)  ncPh.style.display = 'none';
                };
                ncArt.onerror = () => {
                    ncArt.style.display = 'none';
                    if (ncRef) ncRef.style.display = 'none';
                    if (ncPh)  ncPh.style.display  = 'flex';
                };
                ncArt.src = src;
            }
        } else {
            if (ncArt) ncArt.style.display = 'none';
            if (ncRef) ncRef.style.display = 'none';
            if (ncPh)  ncPh.style.display  = 'flex';
        }
    },

    _ncSpawnParticles() {
        const container = this._ncOverlay?.querySelector('#nc-particles');
        if (!container) return;
        const colors = ['#4d8bf5', '#c355ff', '#00e5ff', '#a0c4ff', '#d4aaff'];
        for (let i = 0; i < 38; i++) {
            const p   = document.createElement('div');
            p.className = 'nc-particle';
            const sz  = Math.random() * 3.5 + 0.8;
            const lft = Math.random() * 100;
            const dur = Math.random() * 14 + 8;
            const del = -(Math.random() * 20);
            const col = colors[Math.floor(Math.random() * colors.length)];
            p.style.cssText = `width:${sz}px;height:${sz}px;left:${lft}%;bottom:64px;` +
                `background:${col};animation-duration:${dur}s;animation-delay:${del}s;` +
                `box-shadow:0 0 ${sz * 2}px ${col}`;
            container.appendChild(p);
        }
    },

    _ncStartDancer() {
        // 8-frame female dancer silhouette — monospace ASCII, 7 chars wide x 5 lines
        const FRAMES = [
            // 0 — groove, arms wide
            ['   o   ', '  \\|/  ', '   |   ', '  / \\  ', ' /   \\ '],
            // 1 — right arm sweeps up
            ['   o   ', '  /|   ', '   |/  ', '  / \\  ', ' /   \\ '],
            // 2 — both arms raised wide
            [' \\ o / ', '  \\|/  ', '   |   ', '  / \\  ', ' /   \\ '],
            // 3 — lean right, reaching
            ['   o~  ', '   |/  ', '  / |  ', ' /  \\  ', '/      '],
            // 4 — lean left
            ['  ~o   ', '  \\|   ', '   | \\ ', '   \\   ', '  / \\  '],
            // 5 — full stretch
            ['\\  o  /', ' \\ | / ', '   |   ', '  /|\\  ', ' /   \\ '],
            // 6 — spin step right
            ['   o/  ', '  /|   ', '   |\\  ', '  / \\  ', ' /     '],
            // 7 — hip sway
            ['  _o   ', '  \\|~  ', '   |   ', '  |\\   ', '  / \\  '],
        ];
        const el = this._ncOverlay?.querySelector('#nc-dancer');
        if (!el) return;
        const update = () => {
            if (!this._ncActive) return;
            el.textContent = FRAMES[this._ncDancerFrame % FRAMES.length].join('\n');
            this._ncDancerFrame = (this._ncDancerFrame + 1) % FRAMES.length;
        };
        update();
        this._ncDancerTimer = setInterval(update, 380);
    },

    _ncSetupCanvas() {
        const canvas = this._ncOverlay?.querySelector('#nc-canvas');
        if (!canvas) return;
        this._ncCanvas = canvas;
        this._ncCtx    = canvas.getContext('2d');
        const resize = () => {
            if (!this._ncCanvas) return;
            this._ncCanvas.width  = window.innerWidth;
            this._ncCanvas.height = 156;
        };
        resize();
        this._ncResizeHandler = resize;
        window.addEventListener('resize', resize);
    },

    _ncDrawLoop() {
        if (!this._ncActive) return;
        this._ncAnimFrame = requestAnimationFrame(() => this._ncDrawLoop());
        const canvas = this._ncCanvas;
        const ctx    = this._ncCtx;
        if (!canvas || !ctx) return;
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        const data = this._ncAnalyser && this._ncDataArray
            ? (this._ncAnalyser.getByteFrequencyData(this._ncDataArray), this._ncDataArray)
            : new Uint8Array(256).fill(0);
        if      (this._ncVizStyle === 1) this._ncDrawWaves(ctx, canvas, data);
        else if (this._ncVizStyle === 2) this._ncDrawRainbow(ctx, canvas, data);
        else                             this._ncDrawBars(ctx, canvas, data);
    },

    _ncDrawBars(ctx, canvas, data) {
        const W = canvas.width;
        const H = canvas.height;
        const barCount = Math.min(96, Math.floor(W / 7));
        const barW     = Math.max(2, Math.floor((W - barCount) / barCount));
        const step     = Math.max(1, Math.floor(data.length / barCount));
        for (let i = 0; i < barCount; i++) {
            let sum = 0;
            for (let j = 0; j < step; j++) sum += data[i * step + j] || 0;
            const val  = sum / step / 255;
            const barH = Math.max(2, Math.pow(val, 0.7) * H * 0.96);
            const x    = i * (barW + 1);
            const y    = H - barH;
            let r, g, b;
            if (val > 0.72)      { r = 0;   g = 229; b = 255; }
            else if (val > 0.42) { r = 195; g = 85;  b = 255; }
            else                 { r = 77;  g = 139; b = 245; }
            const grad = ctx.createLinearGradient(x, y, x, H);
            grad.addColorStop(0,    `rgba(${r},${g},${b},0.95)`);
            grad.addColorStop(0.55, `rgba(${r},${g},${b},0.4)`);
            grad.addColorStop(1,    'rgba(0,0,10,0.04)');
            ctx.fillStyle = grad;
            ctx.fillRect(x, y, barW, barH);
            if (val > 0.03) {
                ctx.fillStyle = `rgba(${r},${g},${b},0.35)`;
                ctx.fillRect(x - 1, y - 1, barW + 2, 4);
                ctx.fillStyle = `rgb(${r},${g},${b})`;
                ctx.fillRect(x, y, barW, 2);
            }
        }
    },

    // Style 1 — smooth symmetric waveform (lime/green, mirrored top + bottom)
    _ncDrawWaves(ctx, canvas, data) {
        const W    = canvas.width;
        const H    = canvas.height;
        const mid  = H / 2;
        const amp  = mid * 0.95;
        const pts  = Math.min(220, W);
        const step = Math.max(1, Math.floor(data.length / pts));

        // Smooth the frequency data with a small rolling average
        const vals = new Float32Array(pts);
        for (let i = 0; i < pts; i++) {
            let s = 0, n = 0;
            for (let j = Math.max(0, i * step - 2); j < Math.min(data.length, i * step + 3); j++) {
                s += data[j] || 0; n++;
            }
            vals[i] = Math.pow(s / n / 255, 0.68);   // gamma — lifts quiet passages
        }

        // ── Filled body (upper half + lower mirror in one closed path) ──────────
        const gradFill = ctx.createLinearGradient(0, 0, 0, H);
        gradFill.addColorStop(0,    'rgba(0, 255, 110, 0.82)');
        gradFill.addColorStop(0.38, 'rgba(0, 200, 70,  0.40)');
        gradFill.addColorStop(0.5,  'rgba(0, 140, 40,  0.08)');
        gradFill.addColorStop(0.62, 'rgba(0, 200, 70,  0.40)');
        gradFill.addColorStop(1,    'rgba(0, 255, 110, 0.82)');

        ctx.beginPath();
        // Upper edge — left to right
        ctx.moveTo(0, mid - vals[0] * amp);
        for (let i = 1; i < pts; i++) {
            const x0 = ((i - 1) / (pts - 1)) * W,  y0 = mid - vals[i - 1] * amp;
            const x1 = (i       / (pts - 1)) * W,  y1 = mid - vals[i]     * amp;
            ctx.quadraticCurveTo(x0, y0, (x0 + x1) / 2, (y0 + y1) / 2);
        }
        ctx.lineTo(W, mid - vals[pts - 1] * amp);
        // Lower edge — right to left (mirror)
        ctx.lineTo(W, mid + vals[pts - 1] * amp);
        for (let i = pts - 2; i >= 0; i--) {
            const x0 = ((i + 1) / (pts - 1)) * W,  y0 = mid + vals[i + 1] * amp;
            const x1 = (i       / (pts - 1)) * W,  y1 = mid + vals[i]     * amp;
            ctx.quadraticCurveTo(x0, y0, (x0 + x1) / 2, (y0 + y1) / 2);
        }
        ctx.lineTo(0, mid + vals[0] * amp);
        ctx.closePath();
        ctx.fillStyle = gradFill;
        ctx.fill();

        // ── Glow outline — draw upper + lower edge twice (wide dim → thin bright) ──
        const drawEdge = (sign) => {
            ctx.beginPath();
            ctx.moveTo(0, mid + sign * vals[0] * amp);
            for (let i = 1; i < pts; i++) {
                const x0 = ((i - 1) / (pts - 1)) * W,  y0 = mid + sign * vals[i - 1] * amp;
                const x1 = (i       / (pts - 1)) * W,  y1 = mid + sign * vals[i]     * amp;
                ctx.quadraticCurveTo(x0, y0, (x0 + x1) / 2, (y0 + y1) / 2);
            }
            ctx.lineTo(W, mid + sign * vals[pts - 1] * amp);
        };

        // Soft halo
        drawEdge(+1);
        ctx.strokeStyle = 'rgba(60, 255, 130, 0.28)';
        ctx.lineWidth = 5;
        ctx.stroke();
        // Crisp bright line on top
        drawEdge(+1);
        ctx.strokeStyle = 'rgba(100, 255, 160, 0.95)';
        ctx.lineWidth = 1.5;
        ctx.stroke();

        drawEdge(-1);
        ctx.strokeStyle = 'rgba(60, 255, 130, 0.28)';
        ctx.lineWidth = 5;
        ctx.stroke();
        drawEdge(-1);
        ctx.strokeStyle = 'rgba(100, 255, 160, 0.95)';
        ctx.lineWidth = 1.5;
        ctx.stroke();

        // Subtle centre reference line
        ctx.beginPath();
        ctx.moveTo(0, mid);
        ctx.lineTo(W, mid);
        ctx.strokeStyle = 'rgba(0, 255, 110, 0.15)';
        ctx.lineWidth = 1;
        ctx.stroke();
    },

    // Style 2 — segmented LED rainbow bars (red→green→cyan→blue→pink)
    _ncDrawRainbow(ctx, canvas, data) {
        const W = canvas.width;
        const H = canvas.height;
        const barCount = Math.min(58, Math.floor(W / 12));
        const barW     = Math.max(6, Math.floor((W - barCount * 2) / barCount));
        const step     = Math.max(1, Math.floor(data.length / barCount));
        const segH     = 5;
        const segGap   = 2;
        const segStep  = segH + segGap;
        const maxSegs  = Math.floor((H - 4) / segStep);

        for (let i = 0; i < barCount; i++) {
            let sum = 0;
            for (let j = 0; j < step; j++) sum += data[i * step + j] || 0;
            const val = sum / step / 255;
            const activeSegs = Math.max(0, Math.round(Math.pow(val, 0.72) * maxSegs * 0.97));
            if (activeSegs === 0) continue;

            const x = i * (barW + 2);
            // Hue sweeps: red(0°)→orange→yellow→green→cyan→blue→violet→pink(300°)
            const hue = (i / barCount) * 300;

            for (let s = 0; s < activeSegs; s++) {
                const y = H - (s + 1) * segStep;
                // Top segments are slightly brighter
                const lit = 42 + (s / maxSegs) * 26;
                ctx.fillStyle = `hsl(${hue},100%,${lit}%)`;
                ctx.fillRect(x, y, barW, segH);
            }

            // Bright peak cap on the topmost active segment
            const peakY = H - activeSegs * segStep;
            ctx.fillStyle = `hsl(${hue},100%,82%)`;
            ctx.fillRect(x, peakY, barW, segH);
        }
    },

    // Cycle viz style and update dot indicators + flash label
    _ncSetViz(idx) {
        this._ncVizStyle = idx;
        this._ncSyncVizDots();
        const names = ['Spectrum', 'Pulse', 'Rainbow LED'];
        const label = this._ncOverlay?.querySelector('#nc-viz-label');
        if (label) {
            label.textContent = names[idx];
            label.style.opacity = '1';
            clearTimeout(this._ncVizLabelTimer);
            this._ncVizLabelTimer = setTimeout(() => { if (label) label.style.opacity = '0'; }, 2200);
        }
    },

    _ncSyncVizDots() {
        [0, 1, 2].forEach(i => {
            const dot = this._ncOverlay?.querySelector(`#nc-dot-${i}`);
            if (dot) dot.classList.toggle('active', i === this._ncVizStyle);
        });
    },

    // ─── New Releases ──────────────────────────────────────────────────────

    async renderNewReleases(el) {
        const categories = [
            { key: 'movies',       label: this.t('newreleases.movies'),       icon: 'film'   },
            { key: 'tv',           label: this.t('newreleases.tv'),            icon: 'tv'     },
            { key: 'anime',        label: this.t('newreleases.anime'),         icon: 'star'   },
            { key: 'documentary',  label: this.t('newreleases.documentary'),   icon: 'book'   },
            { key: 'cartoon',      label: this.t('newreleases.cartoon'),       icon: 'layers' },
        ];
        const countries = [
            { code: 'US', name: '🇺🇸 United States'   },
            { code: 'CA', name: '🇨🇦 Canada'           },
            { code: 'GB', name: '🇬🇧 United Kingdom'   },
            { code: 'AU', name: '🇦🇺 Australia'        },
            { code: 'FR', name: '🇫🇷 France'           },
            { code: 'DE', name: '🇩🇪 Germany'          },
            { code: 'ES', name: '🇪🇸 Spain'            },
            { code: 'IT', name: '🇮🇹 Italy'            },
            { code: 'NL', name: '🇳🇱 Netherlands'      },
            { code: 'BE', name: '🇧🇪 Belgium'          },
            { code: 'SE', name: '🇸🇪 Sweden'           },
            { code: 'NO', name: '🇳🇴 Norway'           },
            { code: 'DK', name: '🇩🇰 Denmark'          },
            { code: 'FI', name: '🇫🇮 Finland'          },
            { code: 'AT', name: '🇦🇹 Austria'          },
            { code: 'CH', name: '🇨🇭 Switzerland'      },
            { code: 'PT', name: '🇵🇹 Portugal'         },
            { code: 'IE', name: '🇮🇪 Ireland'          },
            { code: 'PL', name: '🇵🇱 Poland'           },
            { code: 'JP', name: '🇯🇵 Japan'            },
        ];

        // Show loading shell immediately
        el.innerHTML = `<div class="nr-page"><div class="nr-loading">${this.t('player.loading')}</div></div>`;

        // Parallel fetch: genres + results
        const showGenres = this._nrCategory === 'movies' || this._nrCategory === 'tv';
        const params = `category=${this._nrCategory}&country=${this._nrCountry}&genre=${this._nrGenre}&page=${this._nrPage}&mode=${this._nrMode}`;
        const [genres, data] = await Promise.all([
            showGenres ? this.api(`new-releases/genres?category=${this._nrCategory}`) : Promise.resolve(null),
            this.api(`new-releases?${params}`)
        ]);

        // ── Header ──
        let html = `<div class="nr-page">
        <div class="page-header"><h1>${this.t('newreleases.title')}</h1></div>

        <div class="nr-controls">
            <div class="nr-category-tabs">`;

        categories.forEach(cat => {
            const active = this._nrCategory === cat.key ? ' active' : '';
            html += `<button class="nr-tab${active}" onclick="App._nrSetCategory('${cat.key}')">
                <svg style="width:15px;height:15px;stroke:currentColor;fill:none;stroke-width:2;flex-shrink:0"><use href="#icon-${cat.icon}"/></svg>
                ${this.esc(cat.label)}</button>`;
        });

        html += `</div>
            <div class="nr-right-controls">
                <div class="nr-mode-toggle">
                    <button class="nr-mode-btn${this._nrMode === 'recent'   ? ' active' : ''}" onclick="App._nrSetMode('recent')">${this.t('newreleases.recent')}</button>
                    <button class="nr-mode-btn${this._nrMode === 'upcoming' ? ' active' : ''}" onclick="App._nrSetMode('upcoming')">${this.t('newreleases.upcoming')}</button>
                </div>
                <select class="nr-country-select" onchange="App._nrSetCountry(this.value)">`;

        countries.forEach(c => {
            html += `<option value="${c.code}"${this._nrCountry === c.code ? ' selected' : ''}>${c.name}</option>`;
        });

        html += `</select></div></div>`;

        // ── Genre chips (movies/tv only) ──
        if (showGenres && genres && genres.length > 0) {
            html += `<div class="nr-genre-chips">
                <button class="nr-chip${!this._nrGenre ? ' active' : ''}" onclick="App._nrSetGenre('')">${this.t('filter.all')}</button>`;
            genres.forEach(g => {
                html += `<button class="nr-chip${this._nrGenre == g.id ? ' active' : ''}" onclick="App._nrSetGenre(${g.id})">${this.esc(g.name)}</button>`;
            });
            html += `</div>`;
        }

        // ── Results ──
        html += `<div class="nr-results">`;

        if (!data) {
            html += `<div class="empty-state"><h2>${this.t('newreleases.loadError')}</h2></div>`;
        } else if (data.error === 'no_api_key') {
            html += `<div class="nr-no-key">
                <svg style="width:48px;height:48px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-star"/></svg>
                <h3>${this.t('newreleases.noApiKey')}</h3>
                <p>${this.t('newreleases.noApiKeyDesc')}</p>
                <button class="btn-primary" onclick="App.navigate('settings')">${this.t('btn.goToSettings')}</button>
            </div>`;
        } else if (!data.items || data.items.length === 0) {
            html += `<div class="empty-state"><h2>${this.t('newreleases.noResults')}</h2></div>`;
        } else {
            html += `<div class="nr-grid">`;
            data.items.forEach((item, idx) => {
                const rating   = item.rating ? item.rating.toFixed(1) : null;
                const provHtml = this._nrProviderBadges(item.providers);
                html += `<div class="nr-card" onclick="App._nrOpenDetail(${idx})" data-nr-idx="${idx}">
                    <div class="nr-card-poster">
                        ${item.poster
                            ? `<img src="${this.esc(item.poster)}" loading="lazy" alt="" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
                               <span class="nr-card-ph" style="display:none"><svg style="width:32px;height:32px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`
                            : `<span class="nr-card-ph"><svg style="width:32px;height:32px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                        ${provHtml ? `<div class="nr-provider-strip">${provHtml}</div>` : ''}
                    </div>
                    <div class="nr-card-info">
                        <div class="nr-card-title">${this.esc(item.title)}</div>
                        <div class="nr-card-meta">
                            ${item.year ? `<span class="nr-card-year">${item.year}</span>` : ''}
                            ${rating ? `<span class="nr-card-rating">⭐ ${rating}</span>` : ''}
                        </div>
                    </div>
                </div>`;
            });
            html += `</div>`;

            // Store results for detail overlay
            this._nrItems = data.items;

            // Pagination
            if (data.totalPages > 1) {
                html += `<div class="nr-pagination">`;
                if (this._nrPage > 1)
                    html += `<button class="btn-secondary" onclick="App._nrChangePage(${this._nrPage - 1})">${this.t('pagination.prev')}</button>`;
                html += `<span class="nr-page-info">${this.t('newreleases.pageOf').replace('{0}', this._nrPage).replace('{1}', data.totalPages)}</span>`;
                if (this._nrPage < data.totalPages)
                    html += `<button class="btn-secondary" onclick="App._nrChangePage(${this._nrPage + 1})">${this.t('pagination.next')}</button>`;
                html += `</div>`;
            }
        }

        html += `</div></div>`;
        el.innerHTML = html;
    },

    // TMDB provider ID → canonical brand name + color
    _nrProviders: {
        8:    { name: 'Netflix',      bg: '#E50914' },
        175:  { name: 'Netflix',      bg: '#E50914' },
        1796: { name: 'Netflix',      bg: '#E50914' },
        9:    { name: 'Prime',        bg: '#00A8E1' },
        10:   { name: 'Prime',        bg: '#00A8E1' },
        119:  { name: 'Prime',        bg: '#00A8E1' },
        1024: { name: 'Prime',        bg: '#00A8E1' },
        337:  { name: 'Disney+',      bg: '#113CCF' },
        15:   { name: 'Hulu',         bg: '#1CE783', fg: '#000' },
        350:  { name: 'Apple TV+',    bg: '#555555' },
        2:    { name: 'Apple TV+',    bg: '#555555' },
        2552: { name: 'Apple TV+',    bg: '#555555' },
        283:  { name: 'Crunchyroll',  bg: '#F47521' },
        531:  { name: 'Paramount+',   bg: '#0064FF' },
        582:  { name: 'Paramount+',   bg: '#0064FF' },
        1853: { name: 'Paramount+',   bg: '#0064FF' },
        444:  { name: 'Paramount+',   bg: '#0064FF' },
        386:  { name: 'Peacock',      bg: '#FFCC00', fg: '#000' },
        387:  { name: 'Peacock',      bg: '#FFCC00', fg: '#000' },
        1899: { name: 'Max',          bg: '#002B80' },
        384:  { name: 'Max',          bg: '#002B80' },
        192:  { name: 'YouTube',      bg: '#FF0000' },
        188:  { name: 'YouTube',      bg: '#FF0000' },
        3:    { name: 'Google Play',  bg: '#4285F4' },
        279:  { name: 'Rakuten',      bg: '#BF0021' },
        35:   { name: 'Rakuten',      bg: '#BF0021' },
        236:  { name: 'Tubi',         bg: '#FF5A00' },
        300:  { name: 'Pluto TV',     bg: '#242B5E', fg: '#fff' },
        538:  { name: 'Plex',         bg: '#E5A00D', fg: '#000' },
        43:   { name: 'Starz',        bg: '#1C1C1C' },
        44:   { name: 'Starz',        bg: '#1C1C1C' },
        37:   { name: 'Showtime',     bg: '#C8102E' },
        78:   { name: 'Shudder',      bg: '#1A1A1A' },
        190:  { name: 'Cinemax',      bg: '#5C2D91' },
        1770: { name: 'Canal+',       bg: '#000000' },
        257:  { name: 'fuboTV',       bg: '#E8003D' },
        486:  { name: 'ESPN+',        bg: '#CC0000' },
        526:  { name: 'AMC+',         bg: '#2B2B2B' },
        584:  { name: 'discovery+',   bg: '#2175D9' },
        11:   { name: 'MUBI',         bg: '#374151' },
        7:    { name: 'Vudu',         bg: '#3B5EE5' },
        87:   { name: 'Acorn TV',     bg: '#00A878' },
        88:   { name: 'BritBox',      bg: '#FF0037' },
        151:  { name: 'BritBox',      bg: '#FF0037' },
        90:   { name: 'Roku',         bg: '#6F1AB1' },
        269:  { name: 'Funimation',   bg: '#410099' },
        430:  { name: 'HIDIVE',       bg: '#00AEEF' },
        457:  { name: 'ViX',          bg: '#6600CC' },
        613:  { name: 'ViX',          bg: '#6600CC' },
        258:  { name: 'Criterion',    bg: '#111111' },
        84:   { name: 'SundanceNow',  bg: '#C62828' },
        68:   { name: 'MS Store',     bg: '#00A4EF' },
        39:   { name: 'Now TV',       bg: '#00B28A', fg: '#000' },
        58:   { name: 'ITVX',         bg: '#0065BD' },
        62:   { name: 'Channel 4',    bg: '#00A651' },
        61:   { name: 'BBC iPlayer',  bg: '#BB1919' },
        531:  { name: 'Paramount+',   bg: '#0064FF' },
    },

    // Canonical brand name → color — fallback for any provider ID not in _nrProviders.
    // This ensures even unrecognised IDs get the right colour once their name is normalised.
    _nrBrandColors: {
        'Netflix':     { bg: '#E50914' },
        'Prime':       { bg: '#00A8E1' },
        'Disney+':     { bg: '#113CCF' },
        'Hulu':        { bg: '#1CE783', fg: '#000' },
        'Apple TV+':   { bg: '#555555' },
        'Crunchyroll': { bg: '#F47521' },
        'Paramount+':  { bg: '#0064FF' },
        'Peacock':     { bg: '#FFCC00', fg: '#000' },
        'Max':         { bg: '#002B80' },
        'YouTube':     { bg: '#FF0000' },
        'Google Play': { bg: '#4285F4' },
        'Rakuten':     { bg: '#BF0021' },
        'Tubi':        { bg: '#FF5A00' },
        'Pluto TV':    { bg: '#242B5E', fg: '#fff' },
        'Plex':        { bg: '#E5A00D', fg: '#000' },
        'Starz':       { bg: '#1C1C1C' },
        'Showtime':    { bg: '#C8102E' },
        'Shudder':     { bg: '#1A1A1A' },
        'Cinemax':     { bg: '#5C2D91' },
        'Canal+':      { bg: '#000000' },
        'fuboTV':      { bg: '#E8003D' },
        'ESPN+':       { bg: '#CC0000' },
        'AMC+':        { bg: '#2B2B2B' },
        'discovery+':  { bg: '#2175D9' },
        'MUBI':        { bg: '#374151' },
        'Vudu':        { bg: '#3B5EE5' },
        'Acorn TV':    { bg: '#00A878' },
        'BritBox':     { bg: '#FF0037' },
        'Roku':        { bg: '#6F1AB1' },
        'Funimation':  { bg: '#410099' },
        'HIDIVE':      { bg: '#00AEEF' },
        'ViX':         { bg: '#6600CC' },
        'Criterion':   { bg: '#111111' },
        'SundanceNow': { bg: '#C62828' },
        'MS Store':    { bg: '#00A4EF' },
        'Now TV':      { bg: '#00B28A', fg: '#000' },
        'ITVX':        { bg: '#0065BD' },
        'Channel 4':   { bg: '#00A651' },
        'BBC iPlayer': { bg: '#BB1919' },
    },

    // Canonical brand name → streaming search URL (used in detail popup badges)
    _nrProviderUrls: {
        'Netflix':     'https://www.netflix.com/search?q=',
        'Prime':       'https://www.primevideo.com/search/ref=atv_nb_sr?phrase=',
        'Disney+':     'https://www.disneyplus.com/search/',
        'Hulu':        'https://www.hulu.com/search?q=',
        'Apple TV+':   'https://tv.apple.com/search?term=',
        'Crunchyroll': 'https://www.crunchyroll.com/search?q=',
        'Paramount+':  'https://www.paramountplus.com/search/',
        'Peacock':     'https://www.peacocktv.com/stream/search?q=',
        'Max':         'https://www.max.com/search?q=',
        'YouTube':     'https://www.youtube.com/results?search_query=',
        'Google Play': 'https://play.google.com/store/search?c=movies&q=',
        'Rakuten':     'https://www.rakuten.tv/search?q=',
        'Tubi':        'https://tubitv.com/search/',
        'Pluto TV':    'https://pluto.tv/search#',
        'Plex':        'https://watch.plex.tv/search?q=',
        'Starz':       'https://www.starz.com/search?q=',
        'Showtime':    'https://www.sho.com/search?q=',
        'Shudder':     'https://www.shudder.com/search?q=',
        'Canal+':      'https://www.canalplus.com/recherche?q=',
        'fuboTV':      'https://www.fubo.tv/welcome?q=',
        'ESPN+':       'https://www.espnplus.com/search?q=',
        'AMC+':        'https://www.amcplus.com/search?q=',
        'discovery+':  'https://www.discoveryplus.com/search?q=',
        'MUBI':        'https://mubi.com/films?filter%5Bquery%5D=',
        'Vudu':        'https://www.vudu.com/content/movies/search?searchString=',
        'Acorn TV':    'https://acorn.tv/search?q=',
        'BritBox':     'https://www.britbox.com/search?q=',
        'Funimation':  'https://www.funimation.com/search/?q=',
        'HIDIVE':      'https://www.hidive.com/search?q=',
        'Criterion':   'https://www.criterion.com/search#stq=',
        'BBC iPlayer': 'https://www.bbc.co.uk/iplayer/search?q=',
        'ITVX':        'https://www.itv.com/watch?q=',
        'Channel 4':   'https://www.channel4.com/search?q=',
        'Now TV':      'https://www.nowtv.com/search?q=',
        'ViX':         'https://www.vix.com/search?q=',
    },

    // Normalise any raw TMDB provider name to a canonical brand name.
    _nrNormName(raw) {
        const s = raw.trim();
        if (/^Amazon/i.test(s))      return 'Prime';
        if (/^Paramount/i.test(s))   return 'Paramount+';
        if (/^Netflix/i.test(s))     return 'Netflix';
        if (/^Apple TV/i.test(s))    return 'Apple TV+';
        if (/^Peacock/i.test(s))     return 'Peacock';
        if (/^Max\b/i.test(s))       return 'Max';
        if (/^Disney/i.test(s))      return 'Disney+';
        if (/^Hulu/i.test(s))        return 'Hulu';
        if (/^Rakuten/i.test(s))     return 'Rakuten';
        if (/^Showtime/i.test(s))    return 'Showtime';
        if (/^Starz/i.test(s))       return 'Starz';
        if (/^YouTube/i.test(s))     return 'YouTube';
        if (/^fubo/i.test(s))        return 'fuboTV';
        if (/^ESPN/i.test(s))        return 'ESPN+';
        if (/^AMC/i.test(s))         return 'AMC+';
        if (/^discovery/i.test(s))   return 'discovery+';
        if (/^MUBI/i.test(s))        return 'MUBI';
        if (/^Vudu/i.test(s))        return 'Vudu';
        if (/^Fandango/i.test(s))    return 'Vudu';
        if (/^Acorn/i.test(s))       return 'Acorn TV';
        if (/^BritBox/i.test(s))     return 'BritBox';
        if (/^Roku/i.test(s))        return 'Roku';
        if (/^Funimation/i.test(s))  return 'Funimation';
        if (/^HIDIVE/i.test(s))      return 'HIDIVE';
        if (/^Vi[Xx]/i.test(s))      return 'ViX';
        if (/^Criterion/i.test(s))   return 'Criterion';
        if (/^Sundance/i.test(s))    return 'SundanceNow';
        if (/^Microsoft/i.test(s))   return 'MS Store';
        if (/^Now TV/i.test(s))      return 'Now TV';
        if (/^ITV/i.test(s))         return 'ITVX';
        if (/^Channel 4/i.test(s))   return 'Channel 4';
        if (/^BBC/i.test(s))         return 'BBC iPlayer';
        if (/^Canal/i.test(s))       return 'Canal+';
        if (/^Crunchyroll/i.test(s)) return 'Crunchyroll';
        if (/^Pluto/i.test(s))       return 'Pluto TV';
        if (/^Plex/i.test(s))        return 'Plex';
        if (/^Tubi/i.test(s))        return 'Tubi';
        if (/^Shudder/i.test(s))     return 'Shudder';
        if (/^Cinemax/i.test(s))     return 'Cinemax';
        // Strip common qualifier suffixes for anything else
        return s.replace(/\s+(Apple TV Channel|with\s+.+|basic.+|Kids|Premium|Original.*)$/i, '').trim();
    },

    _nrProviderBadges(providers, title) {
        if (!providers || providers.length === 0) return '';
        const seen   = new Set();
        const badges = [];
        for (const p of providers) {
            const def   = this._nrProviders[p.id];
            const brand = def ? def.name : this._nrNormName(p.name);
            if (seen.has(brand)) continue;
            seen.add(brand);
            // Color: ID table → brand color table → grey fallback
            const colorSrc = def ?? this._nrBrandColors[brand] ?? {};
            const bg = colorSrc.bg ?? '#555';
            const fg = colorSrc.fg ?? '#fff';
            const style = `background:${bg};color:${fg}`;
            if (title) {
                const searchUrl = this._nrProviderUrls[brand];
                if (searchUrl) {
                    badges.push(`<a class="nr-provider-badge nr-provider-link" href="${searchUrl}${encodeURIComponent(title)}" target="_blank" rel="noopener" style="${style}" title="Watch on ${this.esc(brand)}">${this.esc(brand)}</a>`);
                } else {
                    badges.push(`<span class="nr-provider-badge" style="${style}">${this.esc(brand)}</span>`);
                }
            } else {
                badges.push(`<span class="nr-provider-badge" style="${style}">${this.esc(brand)}</span>`);
            }
            if (badges.length === 3) break;
        }
        return badges.join('');
    },

    _nrShowNoKeyTooltip(e, btn) {
        e.preventDefault();
        e.stopPropagation();
        const old = document.getElementById('nr-key-tooltip');
        if (old) old.remove();
        const tip = document.createElement('div');
        tip.id = 'nr-key-tooltip';
        tip.className = 'nr-key-tooltip';
        tip.textContent = this.t('newreleases.noTmdbKeyBtn');
        document.body.appendChild(tip);
        const r = btn.getBoundingClientRect();
        tip.style.top  = (r.bottom + 6) + 'px';
        tip.style.left = (r.left + r.width / 2) + 'px';
        tip.style.transform = 'translateX(-50%)';
        setTimeout(() => tip.remove(), 3500);
    },

    _nrSetCategory(cat) {
        this._nrCategory = cat;
        this._nrGenre    = '';
        this._nrPage     = 1;
        this.navigate('newreleases');
    },
    _nrSetCountry(country) {
        this._nrCountry = country;
        this._nrPage    = 1;
        this.navigate('newreleases');
    },
    _nrSetGenre(genre) {
        this._nrGenre = genre;
        this._nrPage  = 1;
        this.navigate('newreleases');
    },
    _nrSetMode(mode) {
        this._nrMode = mode;
        this._nrPage = 1;
        this.navigate('newreleases');
    },
    _nrChangePage(page) {
        this._nrPage = page;
        this.navigate('newreleases');
    },

    _nrOpenDetail(idx) {
        const item = this._nrItems?.[idx];
        if (!item) return;
        const rating   = item.rating ? item.rating.toFixed(1) : null;
        const overview = item.overview || '';
        const tmdbUrl  = `https://www.themoviedb.org/${item.mediaType === 'tv' ? 'tv' : 'movie'}/${item.tmdbId}`;

        const overlay = document.createElement('div');
        overlay.className = 'nr-detail-overlay';
        overlay.innerHTML = `
            <div class="nr-detail-modal" onclick="event.stopPropagation()">
                <button class="nr-detail-close" onclick="this.closest('.nr-detail-overlay').remove()">✕</button>
                <div class="nr-detail-body">
                    <div class="nr-detail-poster">
                        ${item.poster
                            ? `<img src="${this.esc(item.poster)}" alt="" onerror="this.style.display='none'">`
                            : `<span class="nr-card-ph" style="height:200px"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></span>`}
                    </div>
                    <div class="nr-detail-info">
                        <h2 class="nr-detail-title">${this.esc(item.title)}</h2>
                        <div class="nr-detail-meta">
                            ${item.year ? `<span>${item.year}</span>` : ''}
                            ${rating    ? `<span>⭐ ${rating}</span>` : ''}
                            ${item.votes > 0 ? `<span>${item.votes.toLocaleString()} ${this.t('newreleases.votes')}</span>` : ''}
                            <span class="nr-detail-type">${item.mediaType === 'tv' ? this.t('filter.tvShows') : this.t('filter.movies')}</span>
                        </div>
                        ${overview ? `<p class="nr-detail-overview">${this.esc(overview)}</p>` : ''}
                        ${item.providers && item.providers.length > 0 ? `<div class="nr-detail-providers">${this._nrProviderBadges(item.providers, item.title)}</div>` : ''}
                        <div class="nr-detail-actions">
                            <button class="btn-primary" onclick="App._nrSearchLibrary('${this.esc(item.title.replace(/'/g,"\\x27"))}', this)">${this.t('newreleases.findInLibrary')}</button>
                            <button class="nr-trailer-btn" onclick="App._nrOpenTrailer(${item.tmdbId},'${item.mediaType}',this)">▶ ${this.t('newreleases.trailer')}</button>
                            <a class="nr-tmdb-link" href="${tmdbUrl}" target="_blank" rel="noopener">${this.t('newreleases.openTmdb')}</a>
                        </div>
                    </div>
                </div>
            </div>`;
        overlay.addEventListener('click', () => overlay.remove());
        document.body.appendChild(overlay);
    },

    async _nrSearchLibrary(title, btn) {
        btn.disabled    = true;
        btn.textContent = this.t('player.loading');

        // Pass mediaType so anime searches the anime table, not the default non-anime set
        const mtMap = { movies: 'movie', tv: 'tv', anime: 'anime', documentary: 'documentary' };
        const mt    = mtMap[this._nrCategory] ?? null;
        const mtQ   = mt ? `&mediaType=${mt}` : '';

        // Strip subtitle after first colon/dash for a shorter but broader fallback query
        const stripped = title.replace(/\s*[:\u2013\u2014]\s*.+$/, '').trim();

        const trySearch = async (q) => {
            const d = await this.api(`videos?search=${encodeURIComponent(q)}${mtQ}&limit=5`);
            return (d && d.videos && d.videos.length > 0) ? d : null;
        };

        // Try exact title first, then stripped title, then first 3 words
        let data = await trySearch(title);
        if (!data && stripped && stripped !== title) data = await trySearch(stripped);
        if (!data) {
            const words = title.split(/\s+/).slice(0, 3).join(' ');
            if (words !== title && words !== stripped) data = await trySearch(words);
        }

        btn.disabled    = false;
        btn.textContent = this.t('newreleases.findInLibrary');
        if (!data) {
            btn.textContent = this.t('newreleases.notInLibrary');
            setTimeout(() => { btn.textContent = this.t('newreleases.findInLibrary'); }, 2500);
            return;
        }
        const v = data.videos[0];
        btn.closest('.nr-detail-overlay')?.remove();
        this.openVideoDetail(v.id);
    },

    async _nrOpenTrailer(tmdbId, mediaType, btn) {
        const orig    = btn.innerHTML;
        btn.disabled  = true;
        btn.textContent = this.t('player.loading');
        const data = await this.api(`new-releases/trailer?type=${mediaType}&id=${tmdbId}`);
        btn.disabled  = false;
        btn.innerHTML = orig;
        if (!data || !data.url) {
            btn.textContent = this.t('newreleases.noTrailer');
            setTimeout(() => { btn.innerHTML = orig; }, 2500);
            return;
        }
        window.open(data.url, '_blank', 'noopener,noreferrer');
    },

    // ─── Where to Watch (Watchmode) ──────────────────────────────────────────

    // State
    _wtwQuery:  '',
    _wtwType:   '',    // '' | 'movie' | 'tv_series'
    _wtwRegion: 'US',

    // Supported regions on Watchmode free tier
    _wtwRegions: [
        { code: 'US', name: 'United States' },
        { code: 'GB', name: 'United Kingdom' },
        { code: 'CA', name: 'Canada' },
        { code: 'AU', name: 'Australia' },
        { code: 'IN', name: 'India' },
        { code: 'ES', name: 'Spain' },
        { code: 'BR', name: 'Brazil' },
    ],

    // Watchmode source type → label + color
    _wtwTypeStyle: {
        sub:  { label: null /* uses t() */,  bg: '#1a6b3a', fg: '#fff' },
        free: { label: null,                  bg: '#2e5f1e', fg: '#fff' },
        tve:  { label: null,                  bg: '#3a3a6b', fg: '#fff' },
        rent: { label: null,                  bg: '#7a4e1a', fg: '#fff' },
        buy:  { label: null,                  bg: '#5a1a1a', fg: '#fff' },
    },

    async renderWhereToWatch(el) {
        if (!this._initCfg?.watchmodeApiKey) {
            el.innerHTML = `<div class="wtw-page"><div class="page-header"><h1>${this.t('watchmode.title')}</h1></div>
                <div class="nr-no-key">
                    <svg style="width:48px;height:48px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-search"/></svg>
                    <h3>${this.t('watchmode.noKey')}</h3>
                    <p>${this.t('watchmode.noKeyDesc')}</p>
                    <button class="btn-primary" onclick="App.navigate('settings')">${this.t('watchmode.noKeyBtn')}</button>
                </div></div>`;
            return;
        }

        const regionOpts = this._wtwRegions.map(r =>
            `<option value="${r.code}"${r.code === this._wtwRegion ? ' selected' : ''}>${r.name}</option>`
        ).join('');

        el.innerHTML = `<div class="wtw-page">
            <div class="page-header"><h1>${this.t('watchmode.title')}</h1></div>

            <div class="wtw-search-bar">
                <div class="wtw-controls-row">
                    <div class="wtw-type-tabs">
                        <button class="wtw-tab${this._wtwType === '' ? ' active' : ''}" onclick="App._wtwSetType('')">${this.t('watchmode.all')}</button>
                        <button class="wtw-tab${this._wtwType === 'movie' ? ' active' : ''}" onclick="App._wtwSetType('movie')">${this.t('watchmode.movies')}</button>
                        <button class="wtw-tab${this._wtwType === 'tv_series' ? ' active' : ''}" onclick="App._wtwSetType('tv_series')">${this.t('watchmode.tv')}</button>
                    </div>
                    <div class="wtw-region-wrap">
                        <svg style="width:14px;height:14px;stroke:var(--text-secondary);fill:none;stroke-width:2;flex-shrink:0"><use href="#icon-globe"/></svg>
                        <select class="wtw-region-select" onchange="App._wtwSetRegion(this.value)">${regionOpts}</select>
                    </div>
                </div>
                <div class="wtw-input-row">
                    <div class="wtw-input-wrap">
                        <svg class="wtw-input-icon" style="width:16px;height:16px;stroke:var(--text-secondary);fill:none;stroke-width:2"><use href="#icon-search"/></svg>
                        <input id="wtw-input" class="wtw-input" type="text" autocomplete="off" spellcheck="false"
                            placeholder="${this.t('watchmode.searchPlaceholder')}"
                            value="${this.esc(this._wtwQuery)}"
                            onkeydown="if(event.key==='Enter')App._wtwSearch()">
                    </div>
                    <button class="btn-primary wtw-search-btn" onclick="App._wtwSearch()">
                        ${this.t('watchmode.search')}
                    </button>
                </div>
            </div>

            <div id="wtw-results"></div>
            <div class="wtw-attribution">Streaming data powered by <a href="https://www.watchmode.com/" target="_blank" rel="noopener">Watchmode.com</a></div>
        </div>`;

        document.getElementById('wtw-input')?.focus();

        if (this._wtwQuery) this._wtwSearch(true);
    },

    _wtwSetType(type) {
        this._wtwType = type;
        this.navigate('wheretowatch');
    },

    _wtwSetRegion(region) {
        this._wtwRegion = region;
        const resultsEl = document.getElementById('wtw-results');
        if (resultsEl && this._wtwQuery) this._wtwSearch(true);
    },

    async _wtwSearch(silent) {
        const input = document.getElementById('wtw-input');
        const q = (input ? input.value.trim() : this._wtwQuery);
        if (!q) return;
        this._wtwQuery = q;

        const resultsEl = document.getElementById('wtw-results');
        if (!resultsEl) return;

        if (!silent) {
            resultsEl.innerHTML = `<div class="wtw-loading">
                <svg class="spin" style="width:24px;height:24px;stroke:var(--accent);fill:none;stroke-width:2"><use href="#icon-refresh"/></svg>
                ${this.t('watchmode.searching')}
            </div>`;
        }

        const typeParam = this._wtwType ? `&type=${encodeURIComponent(this._wtwType)}` : '';
        const data = await this.api(`watchmode/search?q=${encodeURIComponent(q)}&region=${this._wtwRegion}${typeParam}`);

        if (data?.error === 'no_api_key') {
            resultsEl.innerHTML = `<div class="wtw-error">${this.t('watchmode.noKey')}</div>`;
            return;
        }
        if (data?.error === 'rate_limit') {
            resultsEl.innerHTML = `<div class="wtw-error">${this.t('watchmode.rateLimit')}</div>`;
            return;
        }
        if (data?.error === 'invalid_key') {
            resultsEl.innerHTML = `<div class="wtw-error">${this.t('watchmode.invalidKey')}</div>`;
            return;
        }
        if (data?.error) {
            resultsEl.innerHTML = `<div class="wtw-error">${this.t('watchmode.fetchError')}</div>`;
            return;
        }
        if (!data?.results?.length) {
            resultsEl.innerHTML = `<div class="empty-state"><h2>${this.t('watchmode.noResults')} &ldquo;${this.esc(q)}&rdquo;</h2></div>`;
            return;
        }

        let html = `<p class="wtw-hint">${this.t('watchmode.clickForSources')}</p><div class="wtw-poster-grid">`;
        for (const item of data.results) {
            const typeLabel = this._wtwTypeLabel(item.type);
            const rating    = item.rating ? `<span class="wtw-card-rating">⭐ ${item.rating}</span>` : '';
            const safeTitle = this.esc(item.name?.replace(/'/g,"\\x27") || '');
            const safePoster = item.poster ? this.esc(item.poster) : '';
            html += `<div class="wtw-poster-card" onclick="App._wtwShowSources(${item.id},'${safeTitle}','${this.esc(item.type)}',${item.year||'null'},'${safePoster}')">
                <div class="wtw-poster-img">
                    ${item.poster
                        ? `<img src="${this.esc(item.poster)}" alt="" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">`
                        : ''}
                    <span class="wtw-poster-ph"${item.poster ? ' style="display:none"' : ''}>
                        <svg style="width:36px;height:36px;stroke:rgba(255,255,255,.25);fill:none;stroke-width:1.5"><use href="#icon-film"/></svg>
                    </span>
                </div>
                <div class="wtw-poster-info">
                    <div class="wtw-card-title">${this.esc(item.name || '')}</div>
                    <div class="wtw-card-meta">
                        ${item.year ? `<span>${item.year}</span>` : ''}
                        ${rating}
                        ${typeLabel ? `<span class="wtw-type-badge">${typeLabel}</span>` : ''}
                    </div>
                </div>
            </div>`;
        }
        html += `</div>`;
        resultsEl.innerHTML = html;
    },

    _wtwTypeLabel(type) {
        switch (type) {
            case 'movie':        return this.t('watchmode.typeMovie');
            case 'tv_series':    return this.t('watchmode.typeTv');
            case 'tv_miniseries':return this.t('watchmode.typeMini');
            case 'short_film':   return this.t('watchmode.typeShort');
            default: return type || '';
        }
    },

    async _wtwShowSources(titleId, titleName, titleType, titleYear, posterUrl) {
        // Create overlay immediately
        const overlay = document.createElement('div');
        overlay.className = 'nr-detail-overlay';
        overlay.onclick = () => overlay.remove();
        const typeLabel = this._wtwTypeLabel(titleType);
        overlay.innerHTML = `
            <div class="nr-detail-modal wtw-sources-modal" onclick="event.stopPropagation()">
                <button class="nr-detail-close" onclick="this.closest('.nr-detail-overlay').remove()">✕</button>
                <div class="wtw-sources-header">
                    ${posterUrl ? `<img class="wtw-modal-poster" src="${this.esc(posterUrl)}" alt="" onerror="this.style.display='none'">` : ''}
                    <div class="wtw-sources-title-wrap">
                        <h2 class="nr-detail-title">${this.esc(titleName)}</h2>
                        <div class="nr-detail-meta">
                            ${titleYear ? `<span>${titleYear}</span>` : ''}
                            ${typeLabel ? `<span class="nr-detail-type">${typeLabel}</span>` : ''}
                        </div>
                    </div>
                </div>
                <div id="wtw-sources-body" class="wtw-sources-body">
                    <div class="wtw-loading">
                        <svg class="spin" style="width:24px;height:24px;stroke:var(--accent);fill:none;stroke-width:2"><use href="#icon-refresh"/></svg>
                        ${this.t('watchmode.searching')}
                    </div>
                </div>
            </div>`;
        document.body.appendChild(overlay);

        const data = await this.api(`watchmode/sources?id=${titleId}&region=${this._wtwRegion}`);
        const body = document.getElementById('wtw-sources-body');
        if (!body) return;

        if (data?.error) {
            body.innerHTML = `<div class="wtw-error">${this.t('watchmode.fetchError')}</div>`;
            return;
        }

        const sources = data?.sources || [];
        if (!sources.length) {
            body.innerHTML = `<div class="wtw-no-sources">${this.t('watchmode.noSources')}</div>`;
            return;
        }

        // Group by type
        const groups = {};
        for (const s of sources) {
            const t = s.type || 'other';
            if (!groups[t]) groups[t] = [];
            groups[t].push(s);
        }
        const typeOrder = ['sub', 'free', 'tve', 'rent', 'buy'];

        let html = `<p class="wtw-region-note">${this.t('watchmode.streamingOn')} <strong>${
            this._wtwRegions.find(r => r.code === this._wtwRegion)?.name || this._wtwRegion
        }</strong></p>`;

        for (const type of typeOrder) {
            const grp = groups[type];
            if (!grp) continue;
            const style = this._wtwTypeStyle[type] || { bg: '#555', fg: '#fff' };
            const typeLabel2 = this.t(`watchmode.type.${type}`);
            html += `<div class="wtw-group">
                <div class="wtw-group-label" style="background:${style.bg};color:${style.fg}">${typeLabel2}</div>
                <div class="wtw-source-list">`;
            for (const s of grp) {
                const def      = Object.values(this._nrProviders).find(p => p.name?.toLowerCase() === s.name?.toLowerCase());
                const colorBg  = def?.bg ?? (this._nrBrandColors[s.name]?.bg ?? '#444');
                const colorFg  = def?.fg ?? (this._nrBrandColors[s.name]?.fg ?? '#fff');
                const formatBadge = s.format ? `<span class="wtw-format">${s.format}</span>` : '';
                const priceBadge  = (s.price != null && s.type !== 'sub' && s.type !== 'free')
                    ? `<span class="wtw-price">$${s.price.toFixed(2)}</span>` : '';
                html += `<a class="wtw-source-card" href="${this.esc(s.webUrl)}" target="_blank" rel="noopener">
                    <span class="wtw-source-name" style="background:${colorBg};color:${colorFg}">${this.esc(s.name)}</span>
                    <span class="wtw-source-meta">${formatBadge}${priceBadge}</span>
                    <span class="wtw-watch-now">${this.t('watchmode.watchNow')} <svg style="width:12px;height:12px;stroke:currentColor;fill:none;stroke-width:2.5;vertical-align:-1px"><use href="#icon-external-link"/></svg></span>
                </a>`;
            }
            html += `</div></div>`;
        }

        body.innerHTML = html;
    },

    _wtwShowNoKeyTooltip(e, btn) {
        e.preventDefault();
        e.stopPropagation();
        const old = document.getElementById('wtw-key-tooltip');
        if (old) old.remove();
        const tip = document.createElement('div');
        tip.id = 'wtw-key-tooltip';
        tip.className = 'nr-key-tooltip';
        tip.textContent = this.t('watchmode.noKeyBtn');
        document.body.appendChild(tip);
        const r = btn.getBoundingClientRect();
        tip.style.top  = (r.bottom + 6) + 'px';
        tip.style.left = (r.left + r.width / 2) + 'px';
        tip.style.transform = 'translateX(-50%)';
        setTimeout(() => tip.remove(), 3500);
    },

    // ─── Welcome Modal ───────────────────────────────────────────────────────

    async checkWelcomeModal() {
        // showWelcome comes from _initCfg (config/info fetched at startup) — no extra API call needed
        this._welcomeDismissed = !(this._initCfg?.showWelcome ?? true);
        if (!this._welcomeDismissed) this.showWelcomeModal();
    },

    showWelcomeModal() {
        let overlay = document.getElementById('welcome-modal-overlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'welcome-modal-overlay';
            overlay.className = 'welcome-modal-overlay';
            const version = this._initCfg?.version || '';
            overlay.innerHTML = `
                <div class="welcome-modal">
                    <div class="welcome-modal-header">
                        <div class="welcome-modal-logo">
                            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
                                <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/>
                            </svg>
                        </div>
                        <div class="welcome-modal-title">${this.t('welcome.title')}</div>
                        <span class="welcome-modal-version">${this.t('welcome.version')} ${version}</span>
                    </div>
                    <hr class="welcome-modal-divider">
                    <div class="welcome-modal-section-title">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><polyline points="12 8 12 12 14 14"/></svg>
                        ${this.t('welcome.gettingStarted.title')}
                    </div>
                    <div class="welcome-modal-steps">
                        <div class="welcome-modal-step"><div class="welcome-modal-step-num">1</div><div class="welcome-modal-step-text">${this.t('welcome.gettingStarted.step1')}</div></div>
                        <div class="welcome-modal-step"><div class="welcome-modal-step-num">2</div><div class="welcome-modal-step-text">${this.t('welcome.gettingStarted.step2')}</div></div>
                        <div class="welcome-modal-step"><div class="welcome-modal-step-num">3</div><div class="welcome-modal-step-text">${this.t('welcome.gettingStarted.step3')}</div></div>
                    </div>
                    <hr class="welcome-modal-divider">
                    <div class="welcome-modal-section-title">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>
                        ${this.t('welcome.feedback.title')}
                    </div>
                    <div class="welcome-modal-feedback">
                        <div class="welcome-modal-feedback-desc">${this.t('welcome.feedback.desc')}</div>
                        <a href="${this.t('welcome.feedbackUrl')}" target="_blank" rel="noopener noreferrer" class="welcome-modal-feedback-btn">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>
                            ${this.t('welcome.feedback.btn')}
                        </a>
                    </div>
                    <button class="welcome-modal-ack" onclick="App.dismissWelcomeModal()">${this.t('welcome.acknowledge')}</button>
                </div>`;
            document.body.appendChild(overlay);
        }
        overlay.style.display = 'flex';
    },

    async dismissWelcomeModal() {
        const overlay = document.getElementById('welcome-modal-overlay');
        if (overlay) overlay.style.display = 'none';
        this._welcomeDismissed = true;
        try { await this.apiPost('welcome-dismissed', {}); } catch (e) {}
        // Sync the settings toggle if it's visible
        const tog = document.getElementById('welcome-popup-toggle');
        if (tog) tog.checked = false;
    },

    async toggleWelcomePopup(enabled) {
        this._welcomeDismissed = !enabled;
        try {
            if (enabled) {
                await fetch('/api/welcome-dismissed', { method: 'DELETE' });
                this.showWelcomeModal();
            } else {
                await this.apiPost('welcome-dismissed', {});
                const overlay = document.getElementById('welcome-modal-overlay');
                if (overlay) overlay.style.display = 'none';
            }
        } catch (e) {}
    },
};

document.addEventListener('DOMContentLoaded', () => App.init());
