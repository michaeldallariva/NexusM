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
    mvPage: 1,
    mvSort: 'recent',
    mvArtist: null,
    mvTotal: 0,
    mvPerPage: 60,
    videosPage: 1,
    videosSort: 'recent',
    videosMediaType: null,
    videosTotal: 0,
    videosPerPage: 60,
    _homeRecentTracks: [],

    // Radio state
    isRadioPlaying: false,
    currentRadioStation: null,
    radioStations: [],
    radioCountryFilter: null,
    radioGenreFilter: null,
    radioSearchFilter: '',

    // Podcast state
    podcastFeeds: [],
    podcastCurrentFeed: null,
    podcastEpisodes: [],
    podcastSearchFilter: '',

    userRole: 'guest',
    userName: '',
    userDisplayName: '',

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
                }
                this.securityByPin = !!session.securityByPin;
            } else if (res.status === 401) {
                window.location.href = '/login.html';
                return;
            }
        } catch (e) { /* continue loading */ }

        this.audioPlayer = document.getElementById('audio-player');
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

        // Load saved language
        try {
            const cfgRes = await fetch('/api/config/info');
            if (cfgRes.ok) {
                const cfg = await cfgRes.json();
                this.applyTheme(cfg.theme || 'dark');
                await this.loadLanguage(cfg.language || 'en');
            }
        } catch (e) { /* default to en */ }

        // Stop any active transcode when the browser tab is closed or refreshed
        window.addEventListener('beforeunload', () => {
            if (this._currentTranscodeId) {
                navigator.sendBeacon(`/api/stop-transcode/${this._currentTranscodeId}`, '');
            }
        });

        this.navigate('home');
    },

    // ─── i18n ───────────────────────────────────────────────
    t(key, fallback) {
        return this._lang[key] || fallback || key;
    },

    async loadLanguage(code) {
        try {
            const res = await fetch(`/lang/${code}.json`);
            if (res.ok) {
                this._lang = await res.json();
                this._langCode = code;
            } else {
                console.warn(`Language file ${code}.json not found, falling back to en`);
                if (code !== 'en') {
                    const enRes = await fetch('/lang/en.json');
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
            'home': 'nav.home', 'movies': 'nav.movies', 'music': 'nav.music',
            'musicvideos': 'nav.musicVideos', 'radio': 'nav.radio', 'internettv': 'nav.internetTv', 'podcasts': 'nav.podcasts',
            'pictures': 'nav.pictures', 'ebooks': 'nav.ebooks', 'favourites': 'nav.favourites',
            'playlists': 'nav.playlists', 'mostplayed': 'nav.mostPlayed', 'analysis': 'nav.analysis',
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
        document.body.classList.remove('theme-blue', 'theme-purple', 'theme-emerald');
        if (theme === 'blue') document.body.classList.add('theme-blue');
        else if (theme === 'purple') document.body.classList.add('theme-purple');
        else if (theme === 'emerald') document.body.classList.add('theme-emerald');
        try { localStorage.setItem('nexusm-theme', theme); } catch(e) {}
    },

    async applyMenuVisibility() {
        const config = await this.api('config/info');
        if (!config) return;
        const toggles = {
            movies: config.showMoviesTV,
            musicvideos: config.showMusicVideos,
            radio: config.showRadio,
            internettv: config.showInternetTV,
            podcasts: config.showPodcasts,
            ebooks: config.showEBooks,
            actors: config.showActors
        };
        Object.entries(toggles).forEach(([page, visible]) => {
            // Sidebar links
            const link = document.querySelector(`a[data-page="${page}"]`);
            if (link) link.closest('li').style.display = visible ? '' : 'none';
            // Mobile Library sub-menu buttons
            const btn = document.querySelector(`.mobile-library-menu button[data-page="${page}"]`);
            if (btn) btn.style.display = visible ? '' : 'none';
        });
    },

    applyRoleVisibility() {
        // Hide admin-only pages for guest users
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
        const mvStats = await this.api('musicvideos/stats');
        if (mvStats) {
            const mvBadge = document.getElementById('badge-musicvideos');
            if (mvBadge) mvBadge.textContent = mvStats.totalVideos || 0;
        }
        const videoStats = await this.api('videos/stats');
        if (videoStats) {
            const moviesBadge = document.getElementById('badge-movies');
            if (moviesBadge) moviesBadge.textContent = videoStats.totalVideos || 0;
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
        const libraryPages = ['pictures', 'ebooks', 'musicvideos', 'radio', 'internettv', 'podcasts', 'favourites', 'playlists', 'analysis'];
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

    playVideoStream(videoEl, url) {
        this.stopVideoStream(videoEl);
        if (url.includes('.m3u8') || url.includes('/hls/')) {
            if (typeof Hls !== 'undefined' && Hls.isSupported()) {
                const hls = new Hls({ enableWorker: true });
                videoEl._hlsInstance = hls;
                hls.loadSource(url);
                hls.attachMedia(videoEl);
                hls.on(Hls.Events.MANIFEST_PARSED, () => { videoEl.play(); });
                hls.on(Hls.Events.ERROR, (event, data) => {
                    if (data.fatal) {
                        console.error('HLS fatal error:', data);
                        if (data.type === Hls.ErrorTypes.NETWORK_ERROR) hls.startLoad();
                        else if (data.type === Hls.ErrorTypes.MEDIA_ERROR) hls.recoverMediaError();
                        else { hls.destroy(); videoEl._hlsInstance = null; }
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
        // Block admin-only pages for guests
        const adminOnlyPages = ['analysis', 'settings', 'rescan'];
        if (this.userRole !== 'admin' && adminOnlyPages.includes(page)) {
            page = 'home';
        }
        this.currentPage = page;
        this._mvShuffleMode = false;
        if (this.isRadioPlaying) {
            this.stopRadio();
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
            movies: 'Movies/TV Shows', music: 'Music', musicvideos: 'Music Videos',
            radio: 'Radio', internettv: 'Internet TV', podcasts: 'Podcasts',
            albums: 'Albums', artists: 'Artists', songs: 'Songs', genres: 'Genres',
            pictures: 'Pictures', ebooks: 'eBooks',
            favourites: 'Favourites', playlists: 'Playlists',
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
            case 'recent':     await this.renderRecent(content); break;
            case 'mostplayed': await this.renderMostPlayed(content); break;
            case 'bestrated':  await this.renderBestRated(content); break;
            case 'settings':   await this.renderSettings(content); break;
            case 'rescan':     await this.renderRescan(content); break;

            // New NexusM pages — placeholder UI for now
            case 'movies':      await this.renderMovies(content); break;
            case 'music':       await this.renderMusic(content); break;
            case 'musicvideos': await this.renderMusicVideos(content); break;
            case 'radio':       await this.renderRadio(content); break;
            case 'internettv':  await this.renderInternetTv(content); break;
            case 'podcasts':    await this.renderPodcasts(content); break;
            case 'pictures':    await this.renderPictures(content); break;
            case 'ebooks':      await this.renderEBooks(content); break;
            case 'analysis':    await this.renderAnalysis(content); break;
            case 'insights':    await this.renderInsights(content); break;
            case 'actors':      await this.renderActors(content); break;

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
    },

    stopRadio() {
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
                        <button class="tv-player-fav" id="tv-player-fav" onclick="App.toggleTvFavFromPlayer()" title="Add to Favourites">
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
            const video = document.getElementById('tv-video');
            if (video) this.stopVideoStream(video);
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
        this.podcastCurrentFeed = null;
        this.podcastSearchFilter = '';
        const feeds = await this.api('podcasts');
        if (feeds === null) { el.innerHTML = this.emptyState('Error', 'Could not load podcasts.'); return; }
        this.podcastFeeds = feeds;

        let html = `<div class="radio-header">
            <div class="radio-toolbar">
                <input type="text" class="radio-search-input" id="podcast-search" placeholder="Search subscriptions..." oninput="App.filterPodcasts()" value="">
                <button class="btn-import-m3u" onclick="App.addPodcastFeed()" title="Add RSS Feed">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-plus"/></svg>Add Feed
                </button>
                <label class="btn-import-m3u" style="cursor:pointer" title="Import OPML">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-upload"/></svg>Import OPML
                    <input type="file" accept=".opml,.xml" style="display:none" onchange="App.importOpml(this)">
                </label>
                <button class="btn-fetch-logos" id="btn-podcast-refresh" onclick="App.refreshPodcasts(this)" title="Refresh all feeds">
                    <svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-refresh"/></svg>Refresh
                </button>
            </div>
        </div>`;

        if (feeds.length === 0) {
            html += `<div class="radio-count" style="margin-bottom:8px">No subscriptions yet</div>`;
        } else {
            html += `<div class="radio-count" id="podcast-count">${feeds.length} podcast${feeds.length !== 1 ? 's' : ''}</div>
            <div class="radio-grid" id="podcast-grid">${this.buildPodcastCards(feeds)}</div>`;
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
            const fav = `<button class="radio-card-fav${f.isFavourite ? ' active' : ''}" onclick="App.togglePodcastFav(${f.id},this)" title="Favourite">♥</button>`;
            return `<div class="radio-card" id="podcast-card-${f.id}" ondblclick="App.openPodcast(${f.id})">
                <div class="radio-card-logo" style="position:relative;cursor:pointer" onclick="App.openPodcast(${f.id})">${logo}${placeholder}</div>
                <div class="radio-card-info" style="cursor:pointer" onclick="App.openPodcast(${f.id})">
                    <div class="radio-card-name">${this.esc(f.title)} ${unplayed}</div>
                    <div class="radio-card-desc">${this.esc(f.author || f.category || '')}</div>
                    <div class="radio-card-meta">
                        <span class="radio-card-country">${this.esc(f.category || '')}</span>
                        ${f.episodeCount ? `<span class="radio-card-genre">${f.episodeCount} episodes</span>` : ''}
                    </div>
                </div>
                ${fav}
                <button class="radio-card-play" onclick="App.openPodcast(${f.id})" title="Open">
                    <svg style="width:16px;height:16px;fill:currentColor"><use href="#icon-play"/></svg>
                </button>
                <button class="podcast-delete-btn" onclick="App.deletePodcast(${f.id})" title="Unsubscribe">✕</button>
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
        if (count) count.textContent = `${filtered.length} podcast${filtered.length !== 1 ? 's' : ''}`;
    },

    async openPodcast(feedId) {
        const feed = this.podcastFeeds.find(f => f.id === feedId);
        if (!feed) return;
        this.podcastCurrentFeed = feed;
        const episodes = await this.api(`podcasts/${feedId}/episodes`);
        if (!episodes) return;
        this.podcastEpisodes = episodes;
        const el = document.getElementById('main-content');
        const art = feed.artworkFile ? `/podcastart/${feed.artworkFile}` : '';
        let html = `<div class="podcast-back-btn" onclick="App.renderPodcasts(document.getElementById('main-content'))">
            ← Back to Podcasts
        </div>
        <div style="display:flex;align-items:center;gap:16px;margin-bottom:20px">
            ${art ? `<img src="${art}" alt="" style="width:80px;height:80px;border-radius:10px;object-fit:cover">` : ''}
            <div>
                <div style="font-size:1.2rem;font-weight:700;color:var(--text-primary)">${this.esc(feed.title)}</div>
                <div style="color:var(--text-secondary);font-size:0.85rem">${this.esc(feed.author || '')}</div>
                <div style="color:var(--text-muted);font-size:0.8rem">${episodes.length} episode${episodes.length !== 1 ? 's' : ''}</div>
            </div>
        </div>
        <div class="radio-toolbar" style="margin-bottom:12px">
            <input type="text" class="radio-search-input" id="episode-search" placeholder="Search episodes..." oninput="App.filterEpisodes()">
        </div>
        <div class="podcast-episode-list" id="episode-list">${this.buildEpisodeList(episodes)}</div>`;
        el.innerHTML = html;
    },

    buildEpisodeList(episodes) {
        if (!episodes.length) return `<div style="color:var(--text-muted);padding:24px 0">No episodes found.</div>`;
        return episodes.map(ep => {
            const dur = ep.durationSeconds > 0 ? this.formatDuration(ep.durationSeconds) : '';
            const date = ep.publishDate ? new Date(ep.publishDate).toLocaleDateString() : '';
            const typeBadge = `<span class="podcast-type-badge ${ep.mediaType === 'video' ? 'podcast-type-video' : ''}">${ep.mediaType.toUpperCase()}</span>`;
            const playedClass = ep.isPlayed ? ' podcast-episode-played' : '';
            const resume = ep.playPositionSeconds > 0 && !ep.isPlayed
                ? `<span style="font-size:0.75rem;color:var(--accent);margin-left:6px">▶ ${this.formatDuration(ep.playPositionSeconds)}</span>` : '';
            return `<div class="podcast-episode-row${playedClass}" id="ep-row-${ep.id}">
                <button class="podcast-played-btn" onclick="App.toggleEpisodePlayed(${ep.feedId},${ep.id},this)" title="${ep.isPlayed ? 'Mark unplayed' : 'Mark played'}">
                    ${ep.isPlayed ? '✓' : '○'}
                </button>
                <div class="podcast-episode-info" onclick="App.playPodcastEpisode(${JSON.stringify(ep).replace(/"/g, '&quot;')})">
                    <div class="podcast-episode-title">${this.esc(ep.title)}</div>
                    <div class="podcast-episode-meta">${date}${dur ? ` · ${dur}` : ''}${resume}</div>
                </div>
                ${typeBadge}
                <button class="radio-card-play" style="flex-shrink:0" onclick="App.playPodcastEpisode(${JSON.stringify(ep).replace(/"/g, '&quot;')})" title="Play">
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
        const url = prompt('Enter RSS feed URL:');
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
            alert(`Imported ${data.imported} feeds (${data.skipped} already subscribed, ${data.failed} failed)`);
            await this.renderPodcasts(document.getElementById('main-content'));
        } catch(e) {
            alert('OPML import failed.');
        }
    },

    async refreshPodcasts(btn) {
        if (btn) { btn.disabled = true; btn.innerHTML = '<svg style="width:14px;height:14px;stroke:currentColor;fill:none;stroke-width:2;margin-right:6px"><use href="#icon-refresh"/></svg>Refreshing…'; }
        await this.apiPost('podcasts/refresh');
        setTimeout(async () => {
            await this.renderPodcasts(document.getElementById('main-content'));
        }, 3000);
    },

    playPodcastEpisode(ep) {
        if (typeof ep === 'string') { try { ep = JSON.parse(ep); } catch(e) { return; } }
        if (ep.mediaType === 'video') {
            // Use TV-style fullscreen overlay
            this.playVideoOverlay(ep.mediaUrl, ep.title);
        } else {
            // Use the audio player bar (same as Radio)
            if (this.isRadioPlaying) this.stopRadio();
            this.stopPlayer();
            this.audioPlayer.src = ep.mediaUrl;
            if (ep.playPositionSeconds > 0) this.audioPlayer.currentTime = ep.playPositionSeconds;
            this.audioPlayer.play().catch(() => {});
            const bar = document.querySelector('.player-bar');
            if (bar) { bar.classList.remove('player-hidden'); bar.classList.add('radio-mode'); }
            const titleEl = document.getElementById('player-track-title');
            const artistEl = document.getElementById('player-track-artist');
            if (titleEl) titleEl.textContent = ep.title;
            if (artistEl) artistEl.textContent = this.podcastCurrentFeed ? this.podcastCurrentFeed.title : 'Podcast';
            // Save position every 10s
            this._podcastPositionInterval = setInterval(async () => {
                if (!this.audioPlayer.paused && ep.id) {
                    await this.apiPost(`podcasts/${ep.feedId}/ep/${ep.id}/progress`, { position: Math.floor(this.audioPlayer.currentTime) });
                }
            }, 10000);
        }
    },

    playVideoOverlay(url, title) {
        let overlay = document.getElementById('tv-player-overlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'tv-player-overlay';
            overlay.className = 'tv-player-overlay';
            document.body.appendChild(overlay);
        }
        overlay.innerHTML = `
            <div class="tv-player-header">
                <span class="tv-player-title">${this.esc(title || '')}</span>
                <button class="tv-close-btn" onclick="App.closeTvPlayer()">✕</button>
            </div>
            <video id="podcast-video-el" controls autoplay style="width:100%;height:calc(100% - 44px);background:#000"></video>`;
        overlay.style.display = 'flex';
        const video = document.getElementById('podcast-video-el');
        if (video) this.playVideoStream(video, url);
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
        const suggestions = [
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
            { title: 'SmartLess', author: 'Jason Bateman, Sean Hayes, Will Arnett', category: 'Comedy', url: 'https://feeds.simplecast.com/y1B7lsNM' },
            { title: 'Conan O\'Brien Needs a Friend', author: 'Team Coco', category: 'Comedy', url: 'https://feeds.simplecast.com/dHoohVNH' },
            { title: 'Huberman Lab', author: 'Andrew Huberman', category: 'Health', url: 'https://feeds.megaphone.fm/hubermanlab' },
            { title: 'No Such Thing as a Fish', author: 'QI', category: 'Education', url: 'https://feeds.feedburner.com/NoSuchThingAsAFish' }
        ].filter(s => !subscribedUrls.has(s.url));

        if (!suggestions.length) return '';

        const cards = suggestions.map(s => `
            <div class="radio-card" style="opacity:0.85">
                <div class="radio-card-logo">
                    <div class="radio-card-placeholder">
                        <svg style="width:28px;height:28px;stroke:var(--text-muted);fill:none;stroke-width:1.5"><use href="#icon-podcast"/></svg>
                    </div>
                </div>
                <div class="radio-card-info">
                    <div class="radio-card-name">${this.esc(s.title)}</div>
                    <div class="radio-card-desc">${this.esc(s.author)}</div>
                    <div class="radio-card-meta"><span class="radio-card-country">${this.esc(s.category)}</span></div>
                </div>
                <button class="radio-card-play" onclick="App.subscribeDiscover('${s.url.replace(/'/g,"\\'")}',this)" title="Subscribe"
                    style="width:auto;border-radius:6px;padding:0 10px;font-size:11px">+</button>
            </div>`).join('');

        return `<div class="podcast-discover-section">
            <div class="home-section-header" style="margin-bottom:12px">
                <h2 class="home-section-title">
                    <span class="home-section-icon"><svg style="width:18px;height:18px;stroke:var(--accent);fill:none;stroke-width:2"><use href="#icon-podcast"/></svg></span>
                    Discover Podcasts
                </h2>
            </div>
            <div class="radio-grid">${cards}</div>
        </div>`;
    },

    async subscribeDiscover(url, btn) {
        if (btn) { btn.disabled = true; btn.textContent = '…'; }
        const res = await this.apiPost('podcasts', { url });
        if (res && res.feedId != null) {
            await this.renderPodcasts(document.getElementById('main-content'));
        } else {
            if (res && res.message) alert(res.message);
            if (btn) { btn.disabled = false; btn.textContent = '+'; }
        }
    },

    formatDuration(secs) {
        if (!secs) return '';
        const h = Math.floor(secs / 3600);
        const m = Math.floor((secs % 3600) / 60);
        const s = secs % 60;
        if (h > 0) return `${h}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;
        return `${m}:${String(s).padStart(2,'0')}`;
    },

    // ─── Home Page ───────────────────────────────────────────
    async renderHome(el) {
        // Fetch recent items from all media types in parallel
        const [recentTracks, recentMv, recentPics, recentEbooks, recentVideos] = await Promise.all([
            this.api('tracks/recent?limit=30'),
            this.api('musicvideos?sort=recent&limit=30'),
            this.api('pictures?sort=recent&limit=30'),
            this.api('ebooks?sort=recent&limit=30'),
            this.api('videos?sort=recent&limit=30&grouped=true')
        ]);

        let html = `<div class="page-header"><h1>${this.t('page.home')}</h1>
            <button class="insights-btn" onclick="App.navigate('insights')" title="Smart Insights - Personalized viewing stats">
                <svg class="insights-btn-icon"><use href="#icon-bar-chart"/></svg>
                <span>Smart Insights</span>
            </button></div>`;

        const hasAny = (recentTracks?.length > 0) || (recentMv?.videos?.length > 0)
            || (recentPics?.pictures?.length > 0) || (recentEbooks?.ebooks?.length > 0)
            || (recentVideos?.videos?.length > 0);

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

        // ── Recently Added Movies & TV ──
        const videoList = recentVideos?.videos;
        if (videoList && videoList.length > 0) {
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
                const typeLabel = v.mediaType === 'tv' ? 'TV' : v.mediaType === 'documentary' ? 'Doc' : 'Movie';
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
        if (recentTracks && recentTracks.length > 0) {
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
        if (mvList && mvList.length > 0) {
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
        if (picList && picList.length > 0) {
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
        if (ebookList && ebookList.length > 0) {
            html += `<div class="home-section">
                <div class="home-section-header">
                    <h2 class="home-section-title"><svg class="home-section-icon"><use href="#icon-book"/></svg> ${this.t('section.recentEbooks')}</h2>
                    <button class="home-see-all" onclick="App.navigate('ebooks')">${this.t('btn.seeAll')} &rarr;</button>
                </div>
                <div class="home-row">
                    <button class="home-nav-btn home-nav-left" onclick="App.homeRowScroll(this, -1)">&#10094;</button>
                    <div class="home-row-scroll">`;
            ebookList.forEach(book => {
                const formatBadge = book.format === 'PDF' ? 'pdf' : 'epub';
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
                const typeLabel = item.mediaType === 'tv' ? this.t('insights.tv') : item.mediaType === 'documentary' ? this.t('insights.doc') : this.t('insights.movie');
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
                const typeLabel = v.mediaType === 'tv' ? this.t('insights.tv') : v.mediaType === 'documentary' ? this.t('insights.doc') : this.t('insights.movie');
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
                const typeLabel = v.mediaType === 'tv' ? this.t('insights.tv') : v.mediaType === 'documentary' ? this.t('insights.doc') : this.t('insights.movie');
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
            const typeLabel = v.mediaType === 'tv' ? this.t('insights.tv') : v.mediaType === 'documentary' ? this.t('insights.doc') : this.t('insights.movie');
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
            <button class="btn-secondary" onclick="App.navigate('actors')" style="display:inline-flex;align-items:center;gap:6px">
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
        const kfEl = document.getElementById('actor-knownfor');
        if (kfEl) {
            if (knownfor && knownfor.length > 0) {
                kfEl.innerHTML = knownfor.map(k => {
                    const poster = k.poster ? `/actorphoto/${k.poster}` : '';
                    return `<div style="flex-shrink:0;width:140px;text-align:center">
                        <div style="width:140px;height:200px;border-radius:8px;overflow:hidden;background:var(--bg-card);margin-bottom:6px;border:1px solid var(--border-color)">
                            ${poster
                                ? `<img src="${poster}" style="width:100%;height:100%;object-fit:cover">`
                                : `<div style="display:flex;align-items:center;justify-content:center;height:100%"><svg style="width:40px;height:40px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></div>`}
                        </div>
                        <div style="font-size:0.8rem;font-weight:500;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${k.title}</div>
                        <div style="font-size:0.7rem;color:var(--text-secondary)">${k.mediaType === 'tv' ? 'TV' : 'Movie'} ${k.year ? `(${k.year})` : ''}</div>
                    </div>`;
                }).join('');
            } else {
                kfEl.innerHTML = `<div style="color:var(--text-secondary);font-size:0.9rem;padding:20px">${this.t('actors.noResults') || 'No credits found.'}</div>`;
            }
        }
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
            return items.slice(0, 8).map((i, idx) => {
                const pct = total > 0 ? (i.count / total * 100).toFixed(1) : 0;
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
        html += `<div class="stats-grid">
            <div class="stat-card an-stat-highlight"><div class="stat-value">${t.totalItems.toLocaleString()}</div><div class="stat-label">${this.t('analysis.totalItems')}</div></div>
            <div class="stat-card an-stat-highlight"><div class="stat-value">${this.formatSize(t.totalSize)}</div><div class="stat-label">${this.t('analysis.totalSize')}</div></div>
            <div class="stat-card" style="--an-color:${C.music}"><div class="stat-value" style="color:${C.music}">${t.totalTracks.toLocaleString()}</div><div class="stat-label">${this.t('analysis.musicTracks')}</div></div>
            <div class="stat-card" style="--an-color:${C.movies}"><div class="stat-value" style="color:${C.movies}">${t.totalVideos.toLocaleString()}</div><div class="stat-label">${this.t('analysis.moviesTv')}</div></div>
            <div class="stat-card" style="--an-color:${C.mv}"><div class="stat-value" style="color:${C.mv}">${t.totalMv.toLocaleString()}</div><div class="stat-label">${this.t('analysis.musicVideos')}</div></div>
            <div class="stat-card" style="--an-color:${C.pictures}"><div class="stat-value" style="color:${C.pictures}">${t.totalPictures.toLocaleString()}</div><div class="stat-label">${this.t('analysis.pictures')}</div></div>
            <div class="stat-card" style="--an-color:${C.ebooks}"><div class="stat-value" style="color:${C.ebooks}">${t.totalEbooks.toLocaleString()}</div><div class="stat-label">${this.t('analysis.ebooks')}</div></div>
        </div>`;

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
            html += `<div class="stats-grid">
                <div class="stat-card" style="--an-color:${C.music}"><div class="stat-value" style="color:${C.music}">${t.totalTracks.toLocaleString()}</div><div class="stat-label">Tracks</div></div>
                <div class="stat-card" style="--an-color:${C.music}"><div class="stat-value" style="color:${C.music}">${t.totalAlbums.toLocaleString()}</div><div class="stat-label">Albums</div></div>
                <div class="stat-card" style="--an-color:${C.music}"><div class="stat-value" style="color:${C.music}">${t.totalArtists.toLocaleString()}</div><div class="stat-label">Artists</div></div>
                <div class="stat-card" style="--an-color:${C.music}"><div class="stat-value" style="color:${C.music}">${this.formatDuration(t.totalMusicDuration)}</div><div class="stat-label">Total Duration</div></div>
                <div class="stat-card" style="--an-color:${C.music}"><div class="stat-value" style="color:${C.music}">${this.formatSize(t.totalMusicSize)}</div><div class="stat-label">Storage</div></div>
            </div>`;

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
            html += `<div class="stats-grid">
                <div class="stat-card" style="--an-color:${C.movies}"><div class="stat-value" style="color:${C.movies}">${t.totalVideos.toLocaleString()}</div><div class="stat-label">Total</div></div>
                <div class="stat-card" style="--an-color:${C.movies}"><div class="stat-value" style="color:${C.movies}">${t.totalMovies.toLocaleString()}</div><div class="stat-label">Movies</div></div>
                <div class="stat-card" style="--an-color:${C.movies}"><div class="stat-value" style="color:${C.movies}">${t.totalTvEpisodes.toLocaleString()}</div><div class="stat-label">TV Episodes</div></div>
                <div class="stat-card" style="--an-color:${C.movies}"><div class="stat-value" style="color:${C.movies}">${this.formatDuration(t.totalVideoDuration)}</div><div class="stat-label">Total Duration</div></div>
                <div class="stat-card" style="--an-color:${C.movies}"><div class="stat-value" style="color:${C.movies}">${this.formatSize(t.totalVideoSize)}</div><div class="stat-label">Storage</div></div>
            </div>`;

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
        }

        // ════════════════ MUSIC VIDEOS ANALYSIS ════════════════
        if (t.totalMv > 0) {
            html += `<div class="section-title"><svg class="an-section-icon" style="stroke:${C.mv}"><use href="#icon-video"/></svg> Music Videos Analysis</div>`;
            html += `<div class="stats-grid">
                <div class="stat-card" style="--an-color:${C.mv}"><div class="stat-value" style="color:${C.mv}">${t.totalMv.toLocaleString()}</div><div class="stat-label">Videos</div></div>
                <div class="stat-card" style="--an-color:${C.mv}"><div class="stat-value" style="color:${C.mv}">${this.formatDuration(t.totalMvDuration)}</div><div class="stat-label">Total Duration</div></div>
                <div class="stat-card" style="--an-color:${C.mv}"><div class="stat-value" style="color:${C.mv}">${this.formatSize(t.totalMvSize)}</div><div class="stat-label">Storage</div></div>
            </div>`;

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
                html += `<div class="stats-grid">
                    <div class="stat-card" style="--an-color:${C.pictures}"><div class="stat-value" style="color:${C.pictures}">${t.totalPictures.toLocaleString()}</div><div class="stat-label">Pictures</div></div>
                    <div class="stat-card" style="--an-color:${C.pictures}"><div class="stat-value" style="color:${C.pictures}">${this.formatSize(t.totalPicSize)}</div><div class="stat-label">Storage</div></div>
                </div>`;
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
                html += `<div class="stats-grid">
                    <div class="stat-card" style="--an-color:${C.ebooks}"><div class="stat-value" style="color:${C.ebooks}">${t.totalEbooks.toLocaleString()}</div><div class="stat-label">eBooks</div></div>
                    <div class="stat-card" style="--an-color:${C.ebooks}"><div class="stat-value" style="color:${C.ebooks}">${this.formatSize(t.totalEbookSize)}</div><div class="stat-label">Storage</div></div>
                </div>`;
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

        el.innerHTML = html;
    },

    // ─── Music Library Page (with sub-navigation tabs) ──
    async renderMusic(el) {
        this.musicPage = 1;
        this.musicSort = 'title';
        this.musicSubView = 'all';

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
        const data = await this.api(`tracks?limit=${this.musicPerPage}&page=${this.musicPage}&sort=${this.musicSort}`);
        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load music library.'); return; }
        this.musicTotal = data.total;
        const totalPages = Math.ceil(data.total / this.musicPerPage);
        const sortLabels = { title: this.t('sort.az'), recent: this.t('sort.recent') };

        let html = `<div class="page-header" style="margin-top:4px">
            <div style="font-size:13px;color:var(--text-secondary)">${data.total} tracks in library</div>
            <div class="filter-bar">
            <button class="mv-shuffle-btn" onclick="App.shuffleMusic()" title="Play a random track">
                <svg><use href="#icon-shuffle"/></svg> ${this.t('btn.shufflePlay')}
            </button>`;
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.musicSort === key ? ' active' : ''}" onclick="App.changeMusicSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

        if (data.tracks && data.tracks.length > 0) {
            if (totalPages > 1) {
                html += `<div style="text-align:right;margin-bottom:12px;color:var(--text-muted);font-size:12px">Page ${this.musicPage} of ${totalPages}</div>`;
            }
            html += '<div class="songs-grid">';
            data.tracks.forEach((t, i) => {
                const artSrc = this.getArtUrl(t);
                const dur = this.formatDuration(t.duration);
                const favClass = t.isFavourite ? 'active' : '';
                html += `<div class="song-card" onclick="App.playMusicFromCards(${i})" data-track-id="${t.id}">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`
                        }
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playMusicFromCards(${i})">&#9654;</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta">
                            <span>${this.esc(t.album)}</span>
                            <span>${dur}</span>
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
            <div style="font-size:13px;color:var(--text-secondary)">${data.total} artists</div></div>`;
        if (data.artists && data.artists.length > 0) {
            if (totalPages > 1) {
                html += `<div style="text-align:right;margin-bottom:12px;color:var(--text-muted);font-size:12px">Page ${this._artistsPage} of ${totalPages}</div>`;
            }
            html += '<div class="card-grid">';
            data.artists.forEach(artist => {
                html += `<div class="card" onclick="App.openArtist('${this.esc(artist.name).replace(/'/g, "\\'")}')">
                    <div class="card-cover"><div class="placeholder-icon">&#127908;</div></div>
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
    songsTotal: 0,
    songsPerPage: 100,

    async renderSongs(el) {
        this.songsPage = 1;
        this.songsSort = 'title';
        await this.loadSongsPage(el);
    },

    async loadSongsPage(el) {
        const target = el || document.getElementById('main-content');
        const data = await this.api(`tracks?limit=${this.songsPerPage}&page=${this.songsPage}&sort=${this.songsSort}`);
        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load songs.'); return; }
        this.songsTotal = data.total;
        const totalPages = Math.ceil(data.total / this.songsPerPage);
        const sortLabels = { title: 'A-Z', artist: 'Artist', album: 'Album', recent: 'Recent' };

        let html = `<div class="page-header"><h1>Songs</h1>
            <div class="filter-bar">`;
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.songsSort === key ? ' active' : ''}" onclick="App.changeSongsSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

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
                html += `<div class="song-card" onclick="App.playSongFromCards(${i})" data-track-id="${t.id}">
                    <div class="song-card-art">
                        ${artSrc
                            ? `<img src="${artSrc}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt=""><span class="song-card-placeholder" style="display:none">&#9835;</span>`
                            : `<span class="song-card-placeholder">&#9835;</span>`
                        }
                        <button class="song-card-play" onclick="event.stopPropagation(); App.playSongFromCards(${i})">&#9654;</button>
                        <button class="song-card-add-pl" onclick="App.showAddToPlaylistPopup(${t.id}, this)" title="Add to playlist">+</button>
                    </div>
                    <div class="song-card-info">
                        <div class="song-card-title">${this.esc(t.title)}</div>
                        <div class="song-card-artist">${this.esc(t.artist)}</div>
                        <div class="song-card-meta">
                            <span>${this.esc(t.album)}</span>
                            <span>${dur}</span>
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
        const genres = await this.api('genres');
        let html = `<div class="page-header"><h1>${this.t('page.genres')}</h1></div>`;
        if (genres && genres.length > 0) {
            html += '<div class="genre-grid">';
            genres.forEach(g => {
                html += `<div class="genre-tag" onclick="App.openGenre('${this.esc(g.name)}')">${this.esc(g.name)}<span class="genre-count">${g.count}</span></div>`;
            });
            html += '</div>';
        } else {
            html += this.emptyState(this.t('empty.noGenres.title'), this.t('empty.noGenres.desc'));
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

    // ─── Favourites ──────────────────────────────────────────
    async renderFavourites(el) {
        const [trackData, mvData, videoData, radioData, tvData] = await Promise.all([
            this.api('tracks/favourites?limit=200'),
            this.api('musicvideos/favourites'),
            this.api('videos/favourites'),
            this.api('radio/favourites'),
            this.api('tvchannels/favourites')
        ]);

        const hasTracks = trackData && trackData.tracks && trackData.tracks.length > 0;
        const hasMvs = mvData && mvData.videos && mvData.videos.length > 0;
        const hasVideos = videoData && videoData.videos && videoData.videos.length > 0;
        const hasRadio = radioData && radioData.stations && radioData.stations.length > 0;
        const hasTv = tvData && tvData.channels && tvData.channels.length > 0;

        let html = `<div class="page-header"><h1>${this.t('page.favourites')}</h1></div>`;

        if (!hasTracks && !hasMvs && !hasVideos && !hasRadio && !hasTv) {
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

        el.innerHTML = html;
    },

    playFavTv(id) {
        const channel = (this._favTvChannels || []).find(c => c.id === id);
        if (channel) {
            this.tvChannels = this._favTvChannels;
            this.playTvChannel(id);
        }
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

    // ─── Playlists ───────────────────────────────────────────
    async renderPlaylists(el) {
        const playlists = await this.api('playlists');
        let html = `<div class="page-header"><h1>${this.t('page.playlists')}</h1>
            <button class="btn-primary" onclick="App.createPlaylist()">+ ${this.t('btn.createPlaylist')}</button>
        </div>`;
        if (playlists && playlists.length > 0) {
            html += '<div class="card-grid">';
            playlists.forEach(p => {
                html += `<div class="card" onclick="App.openPlaylist(${p.id})">
                    <div class="card-cover"><div class="placeholder-icon">&#128220;</div></div>
                    <div class="card-info">
                        <div class="card-title">${this.esc(p.name)}</div>
                        <div class="card-subtitle">${p.trackCount} tracks</div>
                    </div>
                </div>`;
            });
            html += '</div>';
        } else {
            html += this.emptyState(this.t('empty.noPlaylists.title'), this.t('empty.noPlaylists.desc'));
        }
        el.innerHTML = html;
    },

    async createPlaylist() {
        const name = prompt('Enter playlist name:');
        if (!name) return;
        await this.apiPost('playlists', { name, description: '' });
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
                    <button onclick="App.renamePlaylist(${id})" title="Rename">&#9998;</button>
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

    async renamePlaylist(id) {
        const nameEl = document.getElementById('pl-name-display');
        const currentName = nameEl ? nameEl.childNodes[0].textContent.trim() : '';
        const newName = prompt('Rename playlist:', currentName);
        if (!newName || newName === currentName) return;
        const res = await this.apiPut(`playlists/${id}`, { name: newName, description: null });
        if (res && res.name) {
            if (nameEl) nameEl.childNodes[0].textContent = res.name + ' ';
            document.getElementById('page-title').innerHTML = `<span>${this.esc(res.name)}</span>`;
        }
    },

    async deletePlaylist(id) {
        if (!confirm('Delete this playlist? This cannot be undone.')) return;
        const res = await this.apiDelete(`playlists/${id}`);
        if (res && res.message) this.renderPage('playlists');
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
                const typeLabel = v.mediaType === 'tv' ? 'TV' : v.mediaType === 'documentary' ? 'Doc' : 'Movie';
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
        let html = `<div class="page-header"><h1>${this.t('page.rescan')}</h1></div>`;
        html += `<div class="settings-section">
            <h3>${this.t('rescan.musicLibraryScan')}</h3>
            <p style="color:var(--text-secondary);font-size:13px;margin-bottom:16px">${this.t('rescan.musicDesc')}</p>
            <button class="btn-primary" onclick="App.startScan()">&#128193; ${this.t('btn.startMusicScan')}</button>
            <div id="scan-status" style="margin-top:16px"></div>
        </div>`;
        html += `<div class="settings-section">
            <h3>${this.t('rescan.picturesLibraryScan')}</h3>
            <p style="color:var(--text-secondary);font-size:13px;margin-bottom:16px">${this.t('rescan.picturesDesc')}</p>
            <button class="btn-primary" onclick="App.startPictureScan()">&#128247; ${this.t('btn.startPicturesScan')}</button>
            <div id="pic-scan-status" style="margin-top:16px"></div>
        </div>`;
        html += `<div class="settings-section">
            <h3>${this.t('rescan.ebooksLibraryScan')}</h3>
            <p style="color:var(--text-secondary);font-size:13px;margin-bottom:16px">${this.t('rescan.ebooksDesc')}</p>
            <button class="btn-primary" onclick="App.startEBookScan()">&#128214; ${this.t('btn.startEBooksScan')}</button>
            <div id="ebook-scan-status" style="margin-top:16px"></div>
        </div>`;
        html += `<div class="settings-section">
            <h3>${this.t('rescan.musicVideosLibraryScan')}</h3>
            <p style="color:var(--text-secondary);font-size:13px;margin-bottom:16px">${this.t('rescan.musicVideosDesc')}</p>
            <div style="display:flex;gap:10px;flex-wrap:wrap">
                <button class="btn-primary" onclick="App.startMvScan()">&#127909; ${this.t('btn.startMusicVideosScan')}</button>
                <button class="btn-primary" style="background:var(--bg-surface);border:1px solid var(--border)" onclick="App.generateMvThumbnails()">&#128247; ${this.t('btn.generateThumbnails')}</button>
            </div>
            <div id="mv-scan-status" style="margin-top:16px"></div>
        </div>`;
        html += `<div class="settings-section">
            <h3>${this.t('rescan.moviesTvScan')}</h3>
            <p style="color:var(--text-secondary);font-size:13px;margin-bottom:16px">${this.t('rescan.moviesTvDesc')}</p>
            <div style="display:flex;gap:10px;flex-wrap:wrap">
                <button class="btn-primary" onclick="App.startVideoScan()">&#127916; ${this.t('btn.startMoviesTvScan')}</button>
                <button class="btn-primary" style="background:var(--bg-surface);border:1px solid var(--border)" onclick="App.generateVideoThumbnails()">&#128247; ${this.t('btn.generateThumbnails')}</button>
            </div>
            <div id="video-scan-status" style="margin-top:16px"></div>
        </div>`;
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

        // ── Server Information ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-settings"/></svg> ${this.t('settings.serverInformation')}</h3>
            <div class="setting-row"><span class="setting-label">${this.t('settings.version')}</span><span class="setting-value">${config.version}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.platform')}</span><span class="setting-value">${this.esc(config.platform)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.framework')}</span><span class="setting-value">.NET ${this.esc(config.framework)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.configFile')}</span><span class="setting-value" style="font-size:12px;word-break:break-all">${this.esc(config.configFile)}</span></div>
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

        // ── User Management (admin only) ──
        if (isAdmin) {
            const users = await this.api('auth/users');
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
                        : `<span class="settings-badge" style="background:var(--accent)">${this.t('settings.guest')}</span>`;
                    html += `<tr id="user-row-${u.id}">
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
            <div class="setting-row"><span class="setting-label">${this.t('settings.theme')}</span><span class="setting-value">${select('theme', config.theme, [{v:'dark',l:this.t('settings.dark')},{v:'blue',l:this.t('settings.blue')},{v:'purple',l:this.t('settings.purple')},{v:'emerald',l:this.t('settings.emerald')}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.defaultView')}</span><span class="setting-value">${select('defaultView', config.defaultView, [{v:'grid',l:this.t('settings.grid')},{v:'list',l:this.t('settings.list')}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.language')}</span><span class="setting-value">${select('language', config.language, [{v:'en',l:'English'},{v:'fr',l:'Francais'},{v:'de',l:'Deutsch'},{v:'es',l:'Espanol'},{v:'it',l:'Italiano'},{v:'pl',l:'Polski'},{v:'pt',l:'Portugues'},{v:'ru',l:'Русский'},{v:'sv',l:'Svenska'}])}</span></div>
        </div>`;

        // ── Menu Components ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-menu"/></svg> ${this.t('settings.menuComponents')}</h3>`;
        const menuItems = [
            { label: this.t('settings.moviesTvShows'), key: 'showMoviesTV', icon: 'film' },
            { label: this.t('settings.musicVideos'), key: 'showMusicVideos', icon: 'video' },
            { label: this.t('settings.radio'), key: 'showRadio', icon: 'radio' },
            { label: this.t('settings.internetTv'), key: 'showInternetTV', icon: 'tv' },
            { label: this.t('settings.ebooks'), key: 'showEBooks', icon: 'book' },
            { label: this.t('settings.actors'), key: 'showActors', icon: 'users' },
            { label: this.t('settings.podcasts'), key: 'showPodcasts', icon: 'podcast' }
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
            { key: 'moviesTVFolders', type: 'moviestv', label: this.t('settings.moviesTvFolders') },
            { key: 'musicVideosFolders', type: 'musicvideos', label: this.t('settings.musicVideosFolders') },
            { key: 'picturesFolders', type: 'pictures', label: this.t('settings.picturesFolders') },
            { key: 'ebooksFolders', type: 'ebooks', label: this.t('settings.ebooksFolders') }
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
        </div>`;

        // ── Metadata Providers ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-film"/></svg> ${this.t('settings.metadataProviders')}</h3>
            <p style="color:var(--text-muted);font-size:12px;margin-bottom:12px">${this.t('settings.metadataProvidersDesc')}</p>
            <div class="setting-row"><span class="setting-label">${this.t('settings.provider')}</span><span class="setting-value">${select('metadataProvider', config.metadataProvider, [{v:'tvmaze',l:this.t('settings.tvmazeFree')},{v:'tmdb',l:this.t('settings.tmdbApiKeyOption')},{v:'none',l:this.t('settings.none')}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.tmdbApiKey')}</span><span class="setting-value">${input('tmdbApiKey', config.tmdbApiKey || '', 'password', 'setting-input-wide')} <a href="https://www.themoviedb.org/settings/api" target="_blank" style="color:var(--accent);font-size:11px;margin-left:8px">${this.t('settings.getFreeKey')}</a></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.fetchOnScan')}</span><span class="setting-value">${toggle('fetchMetadataOnScan', config.fetchMetadataOnScan)} <span class="setting-hint" style="margin-left:8px">${this.t('settings.autoFetchMetadata')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.castPhotos')}</span><span class="setting-value">${toggle('fetchCastPhotos', config.fetchCastPhotos)} <span class="setting-hint" style="margin-left:8px">${this.t('settings.downloadActorPhotos')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.manualFetch')}</span><span class="setting-value"><button class="btn-action" onclick="App.fetchAllVideoMetadata(false)">${this.t('btn.fetchMissing')}</button> <button class="btn-action" onclick="App.fetchAllVideoMetadata(true)" style="margin-left:6px">${this.t('btn.refetchAll')}</button> <span id="metadata-fetch-status" class="setting-hint" style="margin-left:8px"></span></span></div>
        </div>`;

        // ── Playback & Tools ──
        html += `<div class="settings-section">
            <h3><svg class="settings-icon"><use href="#icon-video"/></svg> ${this.t('settings.playbackTools')}</h3>
            <div class="setting-row"><span class="setting-label">${this.t('settings.transcodingEnabled')}</span><span class="setting-value">${toggle('transcodingEnabled', config.transcodingEnabled)}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.transcodeFormat')}</span><span class="setting-value">${select('transcodeFormat', config.transcodeFormat, [{v:'mp3',l:'MP3'},{v:'aac',l:'AAC'},{v:'opus',l:'Opus'}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.transcodeBitrate')}</span><span class="setting-value">${select('transcodeBitrate', config.transcodeBitrate, [{v:'128k',l:'128 kbps'},{v:'192k',l:'192 kbps'},{v:'256k',l:'256 kbps'},{v:'320k',l:'320 kbps'}])}</span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.ffmpegPath')}</span><span class="setting-value">${input('ffmpegPath', config.ffmpegPath || '', 'text', 'setting-input-wide')} <span class="setting-hint">${this.t('settings.emptyAutoDetect')}</span></span></div>
            <div class="setting-row"><span class="setting-label">${this.t('settings.ffmpegStatus')}</span><span class="setting-value">${config.ffmpegAvailable
                ? `<span style="color:var(--success)">${this.t('status.available')}</span>`
                : `<span style="color:var(--warning)">${this.t('status.notInstalled')}</span>
                   ${isAdmin ? `<button class="ffmpeg-download-btn" id="btn-download-ffmpeg" onclick="App.downloadFfmpeg()">${this.t('btn.downloadFfmpeg')}</button>` : ''}
                   ${config.isLinux ? `<span class="setting-hint" style="display:block;margin-top:4px">Linux: auto-downloads GPL static build, or install manually: <code>apt install ffmpeg</code></span>` : ''}`}</span></div>
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
                html += `<div class="setting-row"><span class="setting-label">${this.esc(g.vendor)}</span><span class="setting-value">${this.esc(g.name)} (${g.vramGB}GB) ${badge}</span></div>`;
            });
        } else {
            html += `<div class="setting-row"><span class="setting-label">GPU</span><span class="setting-value" style="color:var(--text-muted)">${this.t('settings.noGpuDetected')}</span></div>`;
        }
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.activeEncoder')}</span><span class="setting-value"><span class="settings-badge" style="background:var(--accent)">${this.esc((config.activeEncoder || 'software').toUpperCase())}</span></span></div>`;
        html += `<div class="setting-group-label">${this.t('settings.encoderSettings')}</div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.preferredEncoder')}</span><span class="setting-value">${select('preferredEncoder', config.preferredEncoder, [{v:'auto',l:this.t('settings.autoDetectBest')},{v:'nvenc',l:this.t('settings.nvidiaNvenc')},{v:'qsv',l:this.t('settings.intelQuickSync')},{v:'amf',l:this.t('settings.amdAmf')},{v:'software',l:this.t('settings.softwareCpu')}])} <span class="setting-hint">${this.t('settings.changesRequireRestart')}</span></span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.softwarePreset')}</span><span class="setting-value">${select('videoPreset', config.videoPreset, [{v:'ultrafast',l:this.t('settings.ultrafast')},{v:'veryfast',l:this.t('settings.veryfast')},{v:'fast',l:this.t('settings.fast')},{v:'medium',l:this.t('settings.medium')},{v:'slow',l:this.t('settings.slow')}])}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.qualityCrf')}</span><span class="setting-value">${input('videoCRF', config.videoCRF, 'number')} <span class="setting-hint">${this.t('settings.crfHint')}</span></span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.hwMaxBitrate')}</span><span class="setting-value">${select('videoMaxrate', config.videoMaxrate, [{v:'3M',l:'3 Mbps'},{v:'5M',l:'5 Mbps'},{v:'8M',l:'8 Mbps'},{v:'10M',l:'10 Mbps'},{v:'15M',l:'15 Mbps'}])}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">${this.t('settings.maxResolution')}</span><span class="setting-value">${select('transcodeMaxHeight', config.transcodeMaxHeight + '', [{v:'0',l:this.t('settings.noLimit')},{v:'1080',l:'1080p'},{v:'720',l:'720p'}])}</span></div>`;
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
            html += `<table class="settings-shares-table"><thead><tr><th>${this.t('table.database')}</th><th>${this.t('table.path')}</th><th>${this.t('table.status')}</th></tr></thead><tbody>`;
            config.databases.forEach(db => {
                const status = db.exists
                    ? `<span class="status-ok">${this.t('nav.online')}</span>`
                    : `<span style="color:var(--warning)">${this.t('settings.notFound')}</span>`;
                html += `<tr>
                    <td style="font-weight:500">${this.esc(db.name)}</td>
                    <td class="settings-share-path">${this.esc(db.path)}</td>
                    <td>${status}</td>
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

        // Load share credentials for each folder type
        this.loadShareCredentials();
    },

    _sharesCache: null,

    async loadShareCredentials() {
        const shares = await this.api('shares');
        this._sharesCache = shares || [];
        const types = ['music', 'moviestv', 'musicvideos', 'pictures', 'ebooks'];
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
        const keyMap = { music: 'musicFolders', moviestv: 'moviesTVFolders', musicvideos: 'musicVideosFolders', pictures: 'picturesFolders', ebooks: 'ebooksFolders' };
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

        const keyMap = { music: 'musicFolders', moviestv: 'moviesTVFolders', musicvideos: 'musicVideosFolders', pictures: 'picturesFolders', ebooks: 'ebooksFolders' };
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
        ['music', 'moviestv', 'musicvideos', 'pictures', 'ebooks'].forEach(t => {
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

    async saveSettings() {
        const v = id => { const e = document.getElementById('cfg-' + id); return e ? e.value : ''; };
        const b = id => { const e = document.getElementById('cfg-' + id); return e ? e.checked : false; };
        const n = id => { const e = document.getElementById('cfg-' + id); return e ? parseInt(e.value) || 0 : 0; };

        const payload = {
            // Server
            serverHost: v('serverHost'),
            serverPort: n('serverPort'),
            workerThreads: n('workerThreads'),
            requestTimeout: n('requestTimeout'),
            sessionTimeout: n('sessionTimeout'),
            showConsole: b('showConsole'),
            openBrowser: b('openBrowser'),
            runOnStartup: b('runOnStartup'),
            // Security
            securityByPin: b('securityByPin'),
            defaultAdminUser: v('defaultAdminUser'),
            ipWhitelist: v('ipWhitelist'),
            // UI
            theme: v('theme'),
            defaultView: v('defaultView'),
            language: v('language'),
            // Menu
            showMoviesTV: b('showMoviesTV'),
            showMusicVideos: b('showMusicVideos'),
            showRadio: b('showRadio'),
            showInternetTV: b('showInternetTV'),
            showEBooks: b('showEBooks'),
            showActors: b('showActors'),
            showPodcasts: b('showPodcasts'),
            // Library
            musicFolders: v('musicFolders'),
            moviesTVFolders: v('moviesTVFolders'),
            musicVideosFolders: v('musicVideosFolders'),
            picturesFolders: v('picturesFolders'),
            ebooksFolders: v('ebooksFolders'),
            autoScanOnStartup: b('autoScanOnStartup'),
            autoScanInterval: n('autoScanInterval'),
            scanThreads: n('scanThreads'),
            // Extensions
            audioExtensions: v('audioExtensions'),
            videoExtensions: v('videoExtensions'),
            musicVideoExtensions: v('musicVideoExtensions'),
            imageExtensions: v('imageExtensions'),
            ebookExtensions: v('ebookExtensions'),
            // Metadata
            metadataProvider: v('metadataProvider'),
            tmdbApiKey: v('tmdbApiKey'),
            fetchMetadataOnScan: b('fetchMetadataOnScan'),
            fetchCastPhotos: b('fetchCastPhotos'),
            // Playback
            transcodingEnabled: b('transcodingEnabled'),
            transcodeFormat: v('transcodeFormat'),
            transcodeBitrate: v('transcodeBitrate'),
            ffmpegPath: v('ffmpegPath'),
            // Video Transcoding
            preferredEncoder: v('preferredEncoder'),
            videoPreset: v('videoPreset'),
            videoCRF: n('videoCRF'),
            videoMaxrate: v('videoMaxrate'),
            transcodeMaxHeight: n('transcodeMaxHeight'),
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
            // Apply theme change and force reload to ensure all elements pick it up
            const newTheme = payload.theme || 'dark';
            this.applyTheme(newTheme);
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
            // Force page reload to fully apply theme across all elements
            setTimeout(() => { window.location.reload(); }, 500);
        } else {
            if (msg) { msg.style.color = 'var(--danger)'; msg.textContent = this.t('status.failedToSave'); }
        }
    },

    async fetchAllVideoMetadata(refetchAll) {
        const msg = document.getElementById('metadata-fetch-status');
        if (msg) { msg.style.color = 'var(--text-muted)'; msg.textContent = refetchAll ? this.t('status.resettingRefetching') : this.t('status.starting'); }
        try {
            const url = refetchAll ? 'videos/fetch-metadata?refetchAll=true' : 'videos/fetch-metadata';
            const res = await this.apiPost(url, {});
            if (res && msg) { msg.style.color = 'var(--success)'; msg.textContent = res.message || this.t('status.metadataFetchStarted'); }
        } catch(e) {
            if (msg) { msg.style.color = 'var(--danger)'; msg.textContent = this.t('status.failedToStartMetadata'); }
        }
    },

    async downloadFfmpeg() {
        const btn = document.getElementById('btn-download-ffmpeg');
        if (!btn) return;
        btn.disabled = true;
        btn.textContent = 'Downloading...';
        btn.classList.add('ffmpeg-downloading');

        try {
            const res = await this.apiPost('config/download-ffmpeg', {});
            if (res && res.success) {
                btn.textContent = 'Installed!';
                btn.classList.remove('ffmpeg-downloading');
                btn.classList.add('ffmpeg-installed');
                // Refresh settings page after short delay to show updated status
                setTimeout(() => this.navigate('settings'), 1500);
            } else {
                btn.textContent = 'Download Failed';
                btn.classList.remove('ffmpeg-downloading');
                btn.classList.add('ffmpeg-failed');
                setTimeout(() => {
                    btn.textContent = 'Download FFmpeg';
                    btn.disabled = false;
                    btn.classList.remove('ffmpeg-failed');
                }, 3000);
            }
        } catch (e) {
            btn.textContent = 'Download Failed';
            btn.classList.remove('ffmpeg-downloading');
            btn.classList.add('ffmpeg-failed');
            setTimeout(() => {
                btn.textContent = 'Download FFmpeg';
                btn.disabled = false;
                btn.classList.remove('ffmpeg-failed');
            }, 3000);
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
                    <select id="new-role">
                        <option value="guest">Guest</option>
                        <option value="admin">Admin</option>
                    </select>
                </div>
            </div>
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

        const result = await this.apiPost('auth/users', { username, pin, userType, displayName: displayName || null });
        if (result && result.message) {
            this.navigate('settings');
        } else if (result && result.error) {
            msgEl.innerHTML = `<span class="user-mgmt-error">${this.esc(result.error)}</span>`;
        }
    },

    showEditUser(id, username, displayName, role) {
        const row = document.getElementById(`user-row-${id}`);
        if (!row) return;
        const isSelf = username === this.userName;
        row.innerHTML = `
            <td><input type="text" class="user-mgmt-inline-input" id="edit-displayname-${id}" value="${this.esc(displayName)}" placeholder="Display Name"></td>
            <td colspan="1"><select class="user-mgmt-inline-input" id="edit-role-${id}">
                <option value="guest" ${role === 'guest' ? 'selected' : ''}>Guest</option>
                <option value="admin" ${role === 'admin' ? 'selected' : ''}>Admin</option>
            </select></td>
            <td><input type="password" class="user-mgmt-inline-input" id="edit-pin-${id}" maxlength="6" placeholder="New PIN (optional)" inputmode="numeric"></td>
            <td colspan="2" class="user-mgmt-actions">
                <button class="user-mgmt-btn user-mgmt-btn-save" onclick="App.submitEditUser(${id})">Save</button>
                <button class="user-mgmt-btn" onclick="App.navigate('settings')">Cancel</button>
            </td>`;
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
    async startVideoScan() {
        const result = await this.apiPost('scan/videos');
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
                    <label class="scan-dialog-option"><input type="checkbox" value="videos" checked> <svg class="scan-dialog-icon"><use href="#icon-film"/></svg> <span>Movies & TV Shows</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="musicvideos" checked> <svg class="scan-dialog-icon"><use href="#icon-video"/></svg> <span>Music Videos</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="pictures" checked> <svg class="scan-dialog-icon"><use href="#icon-image"/></svg> <span>Pictures</span></label>
                    <label class="scan-dialog-option"><input type="checkbox" value="ebooks" checked> <svg class="scan-dialog-icon"><use href="#icon-book"/></svg> <span>eBooks</span></label>
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
        const checked = [...overlay.querySelectorAll('input[type=checkbox]:checked')].map(cb => cb.value);
        overlay.remove();

        if (checked.length === 0) return;

        // Create global progress banner
        this._globalScanTypes = checked;
        this._globalScanDone = new Set();
        this._globalScanStatus = {};
        this._showGlobalScanBanner();

        // Start all selected scans
        const scanEndpoints = {
            music: 'scan',
            videos: 'scan/videos',
            musicvideos: 'scan/musicvideos',
            pictures: 'scan/pictures',
            ebooks: 'scan/ebooks'
        };
        const statusEndpoints = {
            music: 'scan/status',
            videos: 'scan/videos/status',
            musicvideos: 'scan/musicvideos/status',
            pictures: 'scan/pictures/status',
            ebooks: 'scan/ebooks/status'
        };
        const labels = {
            music: 'Music',
            videos: 'Movies & TV',
            musicvideos: 'Music Videos',
            pictures: 'Pictures',
            ebooks: 'eBooks'
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
        if (artists && artists.length > 0) {
            html += '<div class="mv-artist-chips">';
            html += `<button class="filter-chip${!this.mvArtist ? ' active' : ''}" onclick="App.filterMvArtist(null)">${this.t('filter.allArtists')}</button>`;
            artists.forEach(a => {
                const isActive = this.mvArtist === a.name;
                html += `<button class="filter-chip${isActive ? ' active' : ''}" onclick="App.filterMvArtist('${this.esc(a.name)}')">${this.esc(a.name)} <span style="opacity:.6">${a.count}</span></button>`;
            });
            html += '</div>';
        }

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
        const video = await this.api(`musicvideos/${id}`);
        if (!video) return;

        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(video.title)}</span>`;

        let html = `<div class="page-header">
            <div>
                <button class="filter-chip" onclick="App.stopMvShuffle(); App.navigate('musicvideos')" style="margin-bottom:8px">&laquo; Back to Music Videos</button>
                <h1>${this.esc(video.title)}</h1>
                <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">
                    ${this.esc(video.artist)}${video.year ? ' &middot; ' + video.year : ''}
                    &middot; ${this.formatDuration(video.duration)} &middot; ${this.formatSize(video.sizeBytes)}
                </div>
            </div>
        </div>`;

        // Video player
        html += `<div class="mv-player-container">
            <video id="mv-player" controls autoplay class="mv-player">
                <source src="/api/stream-musicvideo/${video.id}" type="video/mp4">
                Your browser does not support the video tag.
            </video>
        </div>`;

        // Community Rating
        const mvRatingSum = await this.api(`ratings/summary/musicvideo/${id}`);
        html += `<div class="settings-section">
            <div class="setting-row">
                <span class="setting-label">Community Rating</span>
                <span class="setting-value">${this.buildRatingWidget(mvRatingSum, 'musicvideo', id)}</span>
            </div>
        </div>`;

        // Video details (collapsible)
        html += `<div class="settings-section">
            <div class="mv-details-toggle" onclick="App.toggleMvDetails()">
                <span class="mv-details-arrow collapsed" id="mv-details-arrow">
                    <svg><use href="#icon-chevron-down"/></svg>
                </span>
                <h3>Video Details</h3>
            </div>
            <div class="mv-details-body collapsed" id="mv-details-body">`;
        html += `<div class="setting-row"><span class="setting-label">Filename</span><span class="setting-value">${this.esc(video.fileName)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Format</span><span class="setting-value">${this.esc(video.format)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Resolution</span><span class="setting-value">${video.resolution || 'Unknown'} (${video.width}x${video.height})</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Codec</span><span class="setting-value">${this.esc(video.codec || 'Unknown')}</span></div>`;
        if (video.bitrate > 0)
            html += `<div class="setting-row"><span class="setting-label">Bitrate</span><span class="setting-value">${Math.round(video.bitrate / 1000)} kbps</span></div>`;
        if (video.audioChannels > 0)
            html += `<div class="setting-row"><span class="setting-label">Audio</span><span class="setting-value">${video.audioChannels} channel${video.audioChannels > 1 ? 's' : ''}${video.audioChannels > 2 ? ' (surround)' : ''}</span></div>`;
        if (video.genre)
            html += `<div class="setting-row"><span class="setting-label">Genre</span><span class="setting-value">${this.esc(video.genre)}</span></div>`;
        if (video.album)
            html += `<div class="setting-row"><span class="setting-label">Album</span><span class="setting-value">${this.esc(video.album)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Size</span><span class="setting-value">${this.formatSize(video.sizeBytes)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">MP4 Compliant</span><span class="setting-value">${video.mp4Compliant ? '<span style="color:var(--success)">Yes</span>' : '<span style="color:var(--warning)">No</span>'}</span></div>`;
        if (video.needsOptimization)
            html += `<div class="setting-row"><span class="setting-label">Needs Optimization</span><span class="setting-value"><span style="color:var(--warning)">Yes</span></span></div>`;
        if (video.moovPosition)
            html += `<div class="setting-row"><span class="setting-label">MOOV Position</span><span class="setting-value">${this.esc(video.moovPosition)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Added</span><span class="setting-value">${new Date(video.dateAdded).toLocaleString()}</span></div>`;
        if (video.lastPlayed)
            html += `<div class="setting-row"><span class="setting-label">Last Played</span><span class="setting-value">${new Date(video.lastPlayed).toLocaleString()}</span></div>`;

        // Fix button for non-compliant
        if (video.needsOptimization) {
            html += `<div style="margin-top:12px;padding-top:12px;border-top:1px solid rgba(255,255,255,.03)">
                <button class="btn-primary" onclick="App.fixMp4(${video.id})" id="fix-mp4-btn">&#128295; Fix MP4 (Remux with FastStart)</button>
                <span id="fix-mp4-status" style="margin-left:12px;font-size:13px;color:var(--text-secondary)"></span>
            </div>`;
        }
        html += '</div></div>';

        // Shuffle mode indicator
        if (this._mvShuffleMode) {
            html += `<div class="mv-shuffle-indicator" id="mv-shuffle-indicator">
                <svg style="width:16px;height:16px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-shuffle"/></svg>
                <span>Shuffle mode active</span>
                <button onclick="App.stopMvShuffle()" style="background:none;border:1px solid rgba(255,255,255,.2);color:var(--text-secondary);padding:3px 10px;border-radius:12px;cursor:pointer;font-size:12px;margin-left:8px">Stop</button>
            </div>`;
        }

        el.innerHTML = html;

        // Attach ended listener for shuffle continuous play
        const player = document.getElementById('mv-player');
        if (player) {
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
        const indicator = document.getElementById('mv-shuffle-indicator');
        if (indicator) indicator.remove();
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
        await this.loadEBooksPage(el);
    },

    async loadEBooksPage(el) {
        const target = el || document.getElementById('main-content');
        let url = `ebooks?limit=${this.ebooksPerPage}&page=${this.ebooksPage}&sort=${this.ebooksSort}`;
        if (this.ebooksCategory) url += `&category=${encodeURIComponent(this.ebooksCategory)}`;

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
                const formatBadge = book.format === 'PDF' ? 'pdf' : 'epub';
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
            html += this.emptyState(this.t('empty.noEbooks.title'), this.t('empty.noEbooks.desc'));
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

        const formatBadge = ebook.format === 'PDF' ? 'pdf' : 'epub';
        let html = `<div class="page-header">
            <div><h1>${this.esc(ebook.title)}</h1>
                <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">
                    ${this.esc(ebook.author || 'Unknown Author')}
                    ${ebook.pageCount > 0 ? ' &middot; ' + ebook.pageCount + ' pages' : ''}
                    &middot; ${this.formatSize(ebook.fileSize)}
                </div>
            </div>
            <div style="display:flex;gap:10px;align-items:center;flex-shrink:0">
                <button class="btn-primary" onclick="App.openEBookReader(${ebook.id})">&#128214; Read ${ebook.format}</button>
                <a href="/api/ebooks/${ebook.id}/download" class="btn-primary" style="text-decoration:none;background:var(--bg-surface);color:var(--text-secondary)" target="_blank">&#128229; Download</a>
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
        html += `<div class="setting-row"><span class="setting-label">Filename</span><span class="setting-value">${this.esc(ebook.fileName)}</span></div>`;
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

    // ─── eBook Reader ────────────────────────────────────
    _epubBook: null,
    _epubRendition: null,
    _ebookReaderOpen: false,
    _ebookPopstateHandler: null,

    async openEBookReader(ebookId) {
        const ebook = await this.api(`ebooks/${ebookId}`);
        if (!ebook) return;

        const isPdf = ebook.format.toUpperCase() === 'PDF';

        let html = `<div class="ebook-reader-overlay" id="ebook-reader-overlay">
            <div class="ebook-reader-toolbar">
                <button class="ebook-reader-btn" onclick="App.closeEBookReader()">
                    <svg><use href="#icon-chevron-left"/></svg> Back
                </button>
                <span class="ebook-reader-title">${this.esc(ebook.title)}</span>
                <div class="ebook-reader-actions">`;

        if (!isPdf) {
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
        } else {
            html += `<div class="ebook-reader-content" id="epub-reader-area">
                <button class="epub-nav-btn epub-nav-prev" onclick="App.epubPrev()">&lsaquo;</button>
                <div id="epub-viewer" style="width:100%;height:100%"></div>
                <button class="epub-nav-btn epub-nav-next" onclick="App.epubNext()">&rsaquo;</button>
            </div>`;
        }

        html += '</div></div>';

        document.body.insertAdjacentHTML('beforeend', html);

        if (!isPdf) {
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

        // Escape key to close
        this._ebookReaderKeyHandler = (e) => {
            if (e.key === 'Escape') this.closeEBookReader();
            if (!isPdf) {
                if (e.key === 'ArrowLeft') this.epubPrev();
                if (e.key === 'ArrowRight') this.epubNext();
            }
        };
        document.addEventListener('keydown', this._ebookReaderKeyHandler);
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

    // ─── Movies / TV Shows ──────────────────────────────────────
    async renderMovies(el) {
        this.videosPage = 1;
        this.videosSort = 'recent';
        this.videosMediaType = null;
        await this.loadVideosPage(el);
    },

    async loadVideosPage(el) {
        const target = el || document.getElementById('main-content');
        let url = `videos?limit=${this.videosPerPage}&page=${this.videosPage}&sort=${this.videosSort}&grouped=true`;
        if (this.videosMediaType) url += `&mediaType=${this.videosMediaType}`;

        const [data, stats] = await Promise.all([
            this.api(url),
            this.api('videos/stats')
        ]);

        if (!data) { target.innerHTML = this.emptyState('Error', 'Could not load videos.'); return; }
        this.videosTotal = data.total;
        const totalPages = Math.ceil(data.total / this.videosPerPage);

        let html = `<div class="page-header"><h1>${this.t('page.moviesTV')}</h1>
            <div class="filter-bar">`;
        const sortLabels = { recent: this.t('sort.recent'), title: this.t('sort.title'), year: this.t('sort.year'), size: this.t('sort.size'), duration: this.t('sort.duration'), series: this.t('sort.series') };
        for (const [key, label] of Object.entries(sortLabels)) {
            html += `<button class="filter-chip${this.videosSort === key ? ' active' : ''}" onclick="App.changeVideosSort('${key}')">${label}</button>`;
        }
        html += `</div></div>`;

        // Media type toggle
        html += '<div style="display:flex;gap:8px;margin-bottom:16px">';
        html += `<button class="filter-chip${!this.videosMediaType ? ' active' : ''}" onclick="App.filterVideosType(null)">${this.t('filter.all')}</button>`;
        html += `<button class="filter-chip${this.videosMediaType === 'movie' ? ' active' : ''}" onclick="App.filterVideosType('movie')">${this.t('filter.movies')}</button>`;
        html += `<button class="filter-chip${this.videosMediaType === 'tv' ? ' active' : ''}" onclick="App.filterVideosType('tv')">${this.t('filter.tvShows')}</button>`;
        html += `<button class="filter-chip${this.videosMediaType === 'documentary' ? ' active' : ''}" onclick="App.filterVideosType('documentary')">Documentaries</button>`;
        html += '</div>';

        // Stats bar
        if (stats && stats.totalVideos > 0) {
            html += `<div class="mv-stats-bar">
                <span>${stats.totalVideos} videos</span>
                <span>${stats.totalMovies} movies</span>
                <span>${stats.totalTvEpisodes} TV episodes</span>
                ${stats.totalSeries > 0 ? `<span>${stats.totalSeries} series</span>` : ''}
                <span>${this.formatSize(stats.totalSize)}</span>
                <span>${this.formatDuration(stats.totalDuration)}</span>
                ${stats.needsOptimization > 0 ? `<span class="mv-stat-warn">${stats.needsOptimization} need optimization</span>` : ''}
            </div>`;
        }

        if (data.videos && data.videos.length > 0) {
            if (totalPages > 1) {
                html += `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;color:var(--text-secondary);font-size:13px">
                    <span>${data.total} videos</span>
                    <span>Page ${this.videosPage} of ${totalPages}</span>
                </div>`;
            }

            html += '<div class="mv-grid">';
            data.videos.forEach(v => {
                // Prefer poster > thumbnail > placeholder
                const thumbSrc = v.posterPath ? `/videometa/${v.posterPath}` : (v.thumbnailPath ? `/videothumb/${v.thumbnailPath}` : '');
                const hasPoster = !!v.posterPath;
                const dur = this.formatDuration(v.duration);
                const resLabel = v.height >= 2160 ? '4K' : v.height >= 1080 ? '1080p' : v.height >= 720 ? '720p' : v.height > 0 ? v.height + 'p' : '';
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
                            <span class="video-type-badge video-type-tv">TV</span>
                            <span class="mv-episode-badge">${v.episodeCount}</span>
                            ${ratingBadge}
                        </div>
                        <div class="mv-card-info">
                            <div class="mv-card-title">${this.esc(v.seriesName)}</div>
                            <div class="mv-card-artist">${seasonLabel} &middot; ${epLabel}</div>
                            <div class="mv-card-meta">
                                <span>${v.year || ''}</span>
                                <span>${this.formatSize(v.sizeBytes)}</span>
                            </div>
                        </div>
                        <button class="mv-card-menu-btn" onclick="event.stopPropagation(); App.showVideoMenu(${v.firstEpisodeId || v.id}, 'tv', event)" title="More options">&#8942;</button>
                    </div>`;
                } else {
                    // Individual video card (movie or ungrouped episode)
                    const typeLabel = v.mediaType === 'tv' ? 'TV' : v.mediaType === 'documentary' ? 'Doc' : 'Movie';
                    const subtitle = v.mediaType === 'tv' && v.seriesName
                        ? `${this.esc(v.seriesName)} S${(v.season||0).toString().padStart(2,'0')}E${(v.episode||0).toString().padStart(2,'0')}`
                        : (v.year ? v.year : '');
                    const vidFavClass = v.isFavourite ? 'active' : '';
                    const watchedBadge = v.isWatched ? `<span class="mv-watched-badge"><svg viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>WATCHED</span>` : '';
                    html += `<div class="mv-card${hasPoster ? ' mv-card-poster' : ''}" onclick="App.openVideoDetail(${v.id})" data-video-id="${v.id}" data-watched="${v.isWatched ? '1' : '0'}">
                        <div class="mv-card-thumb${hasPoster ? ' mv-poster-thumb' : ''}">
                            ${thumbSrc
                                ? `<img src="${thumbSrc}" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" alt="">
                                   <span class="mv-card-placeholder" style="display:none"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`
                                : `<span class="mv-card-placeholder"><svg style="width:48px;height:48px;stroke:currentColor;fill:none;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"><use href="#icon-film"/></svg></span>`}
                            ${watchedBadge}
                            <span class="mv-duration-badge">${dur}</span>
                            ${resLabel ? `<span class="mv-format-badge mv-format-ok">${resLabel}</span>` : ''}
                            <span class="video-type-badge video-type-${v.mediaType}">${typeLabel}</span>
                            <button class="mv-card-play" onclick="event.stopPropagation(); App.playVideo(${v.id})">&#9654;</button>
                            ${ratingBadge}
                        </div>
                        <div class="mv-card-info">
                            <div class="mv-card-title">${this.esc(v.title)}</div>
                            <div class="mv-card-artist">${subtitle}</div>
                            <div class="mv-card-meta">
                                <span>${v.format || ''}</span>
                                <span>${this.formatSize(v.sizeBytes)}</span>
                            </div>
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

        target.innerHTML = html;
    },

    changeVideosSort(sort) {
        this.videosSort = sort;
        this.videosPage = 1;
        this.loadVideosPage();
    },

    filterVideosType(type) {
        this.videosMediaType = type;
        this.videosPage = 1;
        this.loadVideosPage();
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
        this.stopAllMedia();
        const video = await this.api(`videos/${id}`);
        if (!video) return;

        // If multiple audio languages, ask user to select
        const audioTrack = await this._selectAudioTrack(video);
        if (audioTrack < 0) return; // user cancelled

        // Get smart stream info to determine playback method
        const trackParam = audioTrack > 0 ? `?audioTrack=${audioTrack}` : '';
        const streamInfo = await this.api(`stream-info/${id}${trackParam}`);

        const el = document.getElementById('main-content');
        document.getElementById('page-title').innerHTML = `<span>${this.esc(video.title)}</span>`;

        const resLabel = video.height >= 2160 ? '4K' : video.height >= 1080 ? '1080p' : video.height >= 720 ? '720p' : video.height > 0 ? video.height + 'p' : '';
        const episodeInfo = video.mediaType === 'tv' && video.seriesName
            ? `${this.esc(video.seriesName)} &middot; Season ${video.season || '?'} Episode ${video.episode || '?'}`
            : '';

        let html = '';

        // Backdrop banner
        if (video.backdropPath) {
            html += `<div style="height:250px;background:url('/videometa/${video.backdropPath}') center/cover no-repeat;border-radius:12px;margin-bottom:16px;position:relative">
                <div style="position:absolute;bottom:0;left:0;right:0;height:120px;background:linear-gradient(transparent,var(--bg-primary))"></div>
                <button class="filter-chip" onclick="App.navigate('movies')" style="position:absolute;top:12px;left:12px">&laquo; ${this.t('btn.backToMovies')}</button>
            </div>`;
        }

        // Poster + info layout
        if (video.posterPath) {
            html += `<div style="display:flex;gap:20px;margin-bottom:16px">
                <img src="/videometa/${video.posterPath}" style="width:160px;border-radius:8px;flex-shrink:0;box-shadow:0 4px 16px rgba(0,0,0,.4)" alt="">
                <div style="flex:1">
                    ${!video.backdropPath ? `<button class="filter-chip" onclick="App.navigate('movies')" style="margin-bottom:8px">&laquo; ${this.t('btn.backToMovies')}</button>` : ''}
                    <h1 style="margin:0 0 4px 0">${this.esc(video.title)}</h1>
                    <div style="color:var(--text-secondary);font-size:14px;margin-bottom:8px">
                        ${episodeInfo ? episodeInfo + ' &middot; ' : ''}${video.year ? video.year + ' &middot; ' : ''}${this.formatDuration(video.duration)} &middot; ${this.formatSize(video.sizeBytes)}
                        ${resLabel ? ' &middot; ' + resLabel : ''}
                    </div>
                    ${video.rating > 0 ? `<span style="display:inline-block;background:${video.rating >= 7 ? 'rgba(39,174,96,.85)' : video.rating >= 5 ? 'rgba(241,196,15,.85)' : 'rgba(231,76,60,.85)'};color:#fff;padding:3px 10px;border-radius:6px;font-weight:700;font-size:14px;margin-right:8px">${video.rating.toFixed(1)}</span>` : ''}
                    ${video.contentRating ? `<span style="display:inline-block;border:1px solid rgba(255,255,255,.2);padding:2px 8px;border-radius:4px;font-size:12px;color:var(--text-secondary)">${this.esc(video.contentRating)}</span>` : ''}
                    ${video.genre ? `<div style="color:var(--text-secondary);font-size:13px;margin-top:6px">${this.esc(video.genre)}</div>` : ''}
                    ${video.overview ? `<p style="color:var(--text-secondary);font-size:13px;line-height:1.5;margin-top:8px;max-height:120px;overflow-y:auto">${this.esc(video.overview)}</p>` : ''}
                </div>
            </div>`;
        } else {
            // No poster - original simple header
            html += `<div class="page-header">
                <div>
                    ${!video.backdropPath ? `<button class="filter-chip" onclick="App.navigate('movies')" style="margin-bottom:8px">&laquo; ${this.t('btn.backToMovies')}</button>` : ''}
                    <h1>${this.esc(video.title)}</h1>
                    <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">
                        ${episodeInfo ? episodeInfo + ' &middot; ' : ''}${video.year ? video.year + ' &middot; ' : ''}${this.formatDuration(video.duration)} &middot; ${this.formatSize(video.sizeBytes)}
                        ${resLabel ? ' &middot; ' + resLabel : ''}
                    </div>
                </div>
            </div>`;
        }

        // Video player
        const isHLS = streamInfo && streamInfo.type === 'hls';
        html += `<div class="mv-player-container">
            <video id="video-player" controls autoplay class="mv-player">
                ${!isHLS ? `<source src="/api/stream-video/${video.id}${audioTrack > 0 ? '?audioTrack=' + audioTrack : ''}" type="video/mp4">` : ''}
                Your browser does not support the video tag.
            </video>`;
        // Transcode info bar (for HLS transcoded content)
        if (isHLS && streamInfo.transcodeId) {
            const modeLabel = streamInfo.mode === 'transcode-cached' ? 'Cached transcode' : 'Transcoding';
            html += `<div id="transcode-info" style="display:flex;align-items:center;gap:10px;padding:8px 12px;background:rgba(255,255,255,.04);border-radius:0 0 8px 8px;font-size:12px;color:var(--text-secondary)">
                <span>${modeLabel}${streamInfo.reason ? ' - ' + this.esc(streamInfo.reason) : ''}</span>
                ${streamInfo.mode === 'transcode' ? '<button onclick="App.stopCurrentTranscode()" style="margin-left:auto;background:none;border:1px solid rgba(255,255,255,.15);color:var(--text-secondary);padding:3px 10px;border-radius:12px;cursor:pointer;font-size:11px">Stop Transcode</button>' : ''}
            </div>`;
            this._currentTranscodeId = streamInfo.transcodeId;
        }
        html += `</div>`;

        // Overview (only if no poster layout already showing it)
        if (video.overview && !video.posterPath) {
            html += `<div class="settings-section"><p style="color:var(--text-secondary);font-size:14px;line-height:1.6">${this.esc(video.overview)}</p></div>`;
        }

        // Info cards (only show items not already shown in poster layout)
        if (!video.posterPath && (video.director || video.cast || video.genre)) {
            html += '<div class="settings-section">';
            if (video.genre) html += `<div class="setting-row"><span class="setting-label">Genre</span><span class="setting-value">${this.esc(video.genre)}</span></div>`;
            if (video.director) html += `<div class="setting-row"><span class="setting-label">Director</span><span class="setting-value">${this.esc(video.director)}</span></div>`;
            if (video.cast) html += `<div class="setting-row"><span class="setting-label">Cast</span><span class="setting-value">${this.esc(video.cast)}</span></div>`;
            if (video.rating > 0) html += `<div class="setting-row"><span class="setting-label">Score</span><span class="setting-value"><span style="background:${video.rating >= 7 ? 'var(--success)' : video.rating >= 5 ? '#e67e22' : 'var(--danger)'};color:#fff;padding:2px 8px;border-radius:4px;font-weight:600">${video.rating.toFixed(1)}</span> / 10</span></div>`;
            if (video.contentRating) html += `<div class="setting-row"><span class="setting-label">Rating</span><span class="setting-value">${this.esc(video.contentRating)}</span></div>`;
            html += '</div>';
        } else if (video.posterPath && video.director) {
            html += '<div class="settings-section">';
            if (video.director) html += `<div class="setting-row"><span class="setting-label">Director</span><span class="setting-value">${this.esc(video.director)}</span></div>`;
            html += '</div>';
        }

        // Cast section with photos (clickable when actorId present)
        if (video.castJson) {
            try {
                const cast = JSON.parse(video.castJson);
                if (cast.length > 0) {
                    html += '<div class="settings-section"><h3>Cast</h3><div style="display:flex;flex-wrap:wrap;gap:14px;margin-top:8px">';
                    cast.forEach(c => {
                        const photo = c.photo ? `/videometa/${c.photo}` : '';
                        const clickable = c.actorId ? `onclick="App.openActorDetail(${c.actorId})" style="cursor:pointer;text-align:center;width:80px"` : `style="text-align:center;width:80px"`;
                        html += `<div ${clickable}>
                            <div style="width:72px;height:72px;border-radius:50%;overflow:hidden;background:rgba(255,255,255,.06);margin:0 auto 4px">
                                ${photo ? `<img src="${photo}" style="width:100%;height:100%;object-fit:cover" loading="lazy" onerror="this.parentElement.innerHTML='<div style=\\'width:100%;height:100%;display:flex;align-items:center;justify-content:center;font-size:24px;opacity:.3\\'>&#128100;</div>'" alt="">`
                                        : `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;font-size:24px;opacity:.3">&#128100;</div>`}
                            </div>
                            <div style="font-size:11px;font-weight:500;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${this.esc(c.name)}</div>
                            <div style="font-size:10px;color:var(--text-secondary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${this.esc(c.character || '')}</div>
                        </div>`;
                    });
                    html += '</div></div>';
                }
            } catch(e) {}
        }

        // Community Rating
        const ratingSum = await this.api(`ratings/summary/video/${id}`);
        html += `<div class="settings-section">
            <div class="setting-row">
                <span class="setting-label">Community Rating</span>
                <span class="setting-value">${this.buildRatingWidget(ratingSum, 'video', id)}</span>
            </div>
        </div>`;

        // Technical details (collapsible)
        html += `<div class="settings-section">
            <div class="mv-details-toggle" onclick="App.toggleVideoDetails()">
                <span class="mv-details-arrow collapsed" id="video-details-arrow">
                    <svg><use href="#icon-chevron-down"/></svg>
                </span>
                <h3>Technical Details</h3>
            </div>
            <div class="mv-details-body collapsed" id="video-details-body">`;
        html += `<div class="setting-row"><span class="setting-label">Filename</span><span class="setting-value">${this.esc(video.fileName)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Format</span><span class="setting-value">${this.esc(video.format)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Resolution</span><span class="setting-value">${video.resolution || 'Unknown'} (${video.width}x${video.height})</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Video Codec</span><span class="setting-value">${this.esc(video.codec || 'Unknown')}</span></div>`;
        if (video.videoBitrate) html += `<div class="setting-row"><span class="setting-label">Video Bitrate</span><span class="setting-value">${video.videoBitrate} kbps</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Audio Codec</span><span class="setting-value">${this.esc(video.audioCodec || 'Unknown')}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Audio Channels</span><span class="setting-value">${video.audioChannels}</span></div>`;
        if (video.audioLanguages) html += `<div class="setting-row"><span class="setting-label">Audio Languages</span><span class="setting-value">${this.esc(video.audioLanguages)}</span></div>`;
        if (video.subtitleLanguages) html += `<div class="setting-row"><span class="setting-label">Subtitles</span><span class="setting-value">${this.esc(video.subtitleLanguages)}</span></div>`;
        html += `<div class="setting-row"><span class="setting-label">Browser Compatible</span><span class="setting-value">${video.mp4Compliant ? 'Yes' : 'No'}</span></div>`;
        if (streamInfo && streamInfo.mode) html += `<div class="setting-row"><span class="setting-label">Stream Mode</span><span class="setting-value">${this.esc(streamInfo.mode)}${streamInfo.reason ? ' (' + this.esc(streamInfo.reason) + ')' : ''}</span></div>`;
        html += '</div></div>';

        el.innerHTML = html;

        // If HLS stream, use playVideoStream to load via HLS.js
        if (isHLS && streamInfo.playlistUrl) {
            const videoEl = document.getElementById('video-player');
            if (videoEl) this.playVideoStream(videoEl, streamInfo.playlistUrl);
        }
    },

    async openSeriesDetail(seriesName) {
        this.stopAllMedia();
        const data = await this.api(`videos?series=${encodeURIComponent(seriesName)}&sort=series&limit=500`);
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
        });
        const seasonNums = Object.keys(seasons).map(Number).sort((a, b) => a - b);

        let html = '';

        // Backdrop banner
        if (seriesMeta.backdropPath) {
            html += `<div style="height:220px;background:url('/videometa/${seriesMeta.backdropPath}') center/cover no-repeat;border-radius:12px;margin-bottom:16px;position:relative">
                <div style="position:absolute;bottom:0;left:0;right:0;height:100px;background:linear-gradient(transparent,var(--bg-primary))"></div>
                <button class="filter-chip" onclick="App.navigate('movies')" style="position:absolute;top:12px;left:12px">&laquo; ${this.t('btn.backToMovies')}</button>
            </div>`;
        }

        // Poster + series info
        if (seriesMeta.posterPath) {
            html += `<div style="display:flex;gap:20px;margin-bottom:16px">
                <img src="/videometa/${seriesMeta.posterPath}" style="width:140px;border-radius:8px;flex-shrink:0;box-shadow:0 4px 16px rgba(0,0,0,.4)" alt="">
                <div style="flex:1">
                    ${!seriesMeta.backdropPath ? `<button class="filter-chip" onclick="App.navigate('movies')" style="margin-bottom:8px">&laquo; ${this.t('btn.backToMovies')}</button>` : ''}
                    <h1 style="margin:0 0 4px 0">${this.esc(seriesName)}</h1>
                    <div style="color:var(--text-secondary);font-size:14px;margin-bottom:8px">
                        ${seasonNums.length} Season${seasonNums.length !== 1 ? 's' : ''} &middot; ${episodes.length} Episode${episodes.length !== 1 ? 's' : ''} &middot; ${this.formatDuration(totalDuration)} &middot; ${this.formatSize(episodes.reduce((a, e) => a + (e.sizeBytes || 0), 0))}
                    </div>
                    ${seriesMeta.rating > 0 ? `<span style="display:inline-block;background:${seriesMeta.rating >= 7 ? 'rgba(39,174,96,.85)' : seriesMeta.rating >= 5 ? 'rgba(241,196,15,.85)' : 'rgba(231,76,60,.85)'};color:#fff;padding:3px 10px;border-radius:6px;font-weight:700;font-size:14px;margin-right:8px">${seriesMeta.rating.toFixed(1)}</span>` : ''}
                    ${seriesMeta.contentRating ? `<span style="display:inline-block;border:1px solid rgba(255,255,255,.2);padding:2px 8px;border-radius:4px;font-size:12px;color:var(--text-secondary)">${this.esc(seriesMeta.contentRating)}</span>` : ''}
                    ${seriesMeta.genre ? `<div style="color:var(--text-secondary);font-size:13px;margin-top:6px">${this.esc(seriesMeta.genre)}</div>` : ''}
                    ${seriesMeta.overview ? `<p style="color:var(--text-secondary);font-size:13px;line-height:1.5;margin-top:8px;max-height:100px;overflow-y:auto">${this.esc(seriesMeta.overview)}</p>` : ''}
                </div>
            </div>`;
        } else {
            html += `<div class="page-header">
                <div>
                    ${!seriesMeta.backdropPath ? `<button class="filter-chip" onclick="App.navigate('movies')" style="margin-bottom:8px">&laquo; ${this.t('btn.backToMovies')}</button>` : ''}
                    <h1>${this.esc(seriesName)}</h1>
                    <div style="color:var(--text-secondary);font-size:14px;margin-top:4px">
                        ${seasonNums.length} Season${seasonNums.length !== 1 ? 's' : ''} &middot; ${episodes.length} Episode${episodes.length !== 1 ? 's' : ''} &middot; ${this.formatDuration(totalDuration)} &middot; ${this.formatSize(episodes.reduce((a, e) => a + (e.sizeBytes || 0), 0))}
                    </div>
                </div>
            </div>`;
        }

        // Cast section with photos (clickable when actorId present)
        if (seriesMeta.castJson) {
            try {
                const cast = JSON.parse(seriesMeta.castJson);
                if (cast.length > 0) {
                    html += '<div class="settings-section"><h3>Cast</h3><div style="display:flex;flex-wrap:wrap;gap:14px;margin-top:8px">';
                    cast.forEach(c => {
                        const photo = c.photo ? `/videometa/${c.photo}` : '';
                        const clickable = c.actorId ? `onclick="App.openActorDetail(${c.actorId})" style="cursor:pointer;text-align:center;width:80px"` : `style="text-align:center;width:80px"`;
                        html += `<div ${clickable}>
                            <div style="width:72px;height:72px;border-radius:50%;overflow:hidden;background:rgba(255,255,255,.06);margin:0 auto 4px">
                                ${photo ? `<img src="${photo}" style="width:100%;height:100%;object-fit:cover" loading="lazy" onerror="this.parentElement.innerHTML='<div style=\\'width:100%;height:100%;display:flex;align-items:center;justify-content:center;font-size:24px;opacity:.3\\'>&#128100;</div>'" alt="">`
                                        : `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;font-size:24px;opacity:.3">&#128100;</div>`}
                            </div>
                            <div style="font-size:11px;font-weight:500;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${this.esc(c.name)}</div>
                            <div style="font-size:10px;color:var(--text-secondary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${this.esc(c.character || '')}</div>
                        </div>`;
                    });
                    html += '</div></div>';
                }
            } catch(e) {}
        }

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
                            ${epDur}${resLabel ? ' &middot; ' + resLabel : ''} &middot; ${ep.format || ''} &middot; ${this.formatSize(ep.sizeBytes)}
                        </div>
                    </div>
                    <button class="mv-card-play" onclick="event.stopPropagation(); App.openVideoDetail(${ep.id})" style="position:static;width:36px;height:36px;border-radius:50%;background:var(--accent);border:none;color:#fff;cursor:pointer;font-size:14px;display:flex;align-items:center;justify-content:center;opacity:.8;flex-shrink:0">&#9654;</button>
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

    async stopCurrentTranscode() {
        if (!this._currentTranscodeId) return;
        try {
            await this.apiPost(`stop-transcode/${this._currentTranscodeId}`, {});
            const info = document.getElementById('transcode-info');
            if (info) info.innerHTML = '<span style="color:var(--warning)">Transcode stopped</span>';
            // Stop the video player
            const videoEl = document.getElementById('video-player');
            if (videoEl) this.stopVideoStream(videoEl);
        } catch (e) {
            console.error('Failed to stop transcode:', e);
        }
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
            `<div class="setting-row"><span class="setting-label">Filename</span><span class="setting-value">${this.esc(metadata.fileName)}</span></div>`,
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
            const num = showTrackNum ? (t.trackNumber || i + 1) : (i + 1);
            const favClass = t.isFavourite ? 'active' : '';
            return `<tr onclick="App.playFromList(${i})" data-track-id="${t.id}">
                <td class="track-number">${num}</td>
                <td class="track-title">${this.esc(t.title)}</td>
                <td>${this.esc(t.artist)}</td>
                <td>${this.esc(t.album)}</td>
                <td class="track-duration">${dur}</td>
                <td class="track-actions">
                    <button class="track-fav-btn ${favClass}" onclick="event.stopPropagation(); App.toggleFav(${t.id}, this)">&#10084;</button>
                    <button class="track-fav-btn" onclick="App.showAddToPlaylistPopup(${t.id}, this)" title="Add to playlist" style="color:var(--text-muted)">+</button>
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
        }
        // Stop any playing video before starting audio
        document.querySelectorAll('video').forEach(v => { v.pause(); v.removeAttribute('src'); v.load(); });
        this.currentTrack = track;
        this.audioPlayer.src = `/api/stream/${track.id}`;
        this.audioPlayer.play();
        this.isPlaying = true;
        document.getElementById('player-bar').classList.remove('player-hidden');
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
        this.playlist = [];
        this.playIndex = -1;
        document.getElementById('player-bar').classList.add('player-hidden');
    },

    togglePlay() {
        if (!this.audioPlayer.src) return;
        const svgStyle = 'width:22px;height:22px;stroke:currentColor;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round';
        if (this.isPlaying) { this.audioPlayer.pause(); this.isPlaying = false; document.getElementById('btn-play').innerHTML = `<svg style="${svgStyle}"><use href="#icon-play"/></svg>`; }
        else { this.audioPlayer.play(); this.isPlaying = true; document.getElementById('btn-play').innerHTML = `<svg style="${svgStyle}"><use href="#icon-pause"/></svg>`; }
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

        let totalResults = (data.artists?.length || 0) + (data.albums?.length || 0)
            + (data.tracks?.length || 0) + (data.pictures?.length || 0)
            + (data.ebooks?.length || 0) + (data.musicVideos?.length || 0)
            + (data.videos?.length || 0) + (data.actors?.length || 0);

        let html = `<div class="page-header"><h1>Search Results</h1>
            <span style="color:var(--text-secondary);font-size:13px">${totalResults} result${totalResults !== 1 ? 's' : ''}</span>
        </div>`;

        // Artists
        if (data.artists && data.artists.length > 0) {
            html += '<div class="section-title">Artists</div><div class="card-grid">';
            data.artists.forEach(a => { html += `<div class="card" onclick="App.openArtist(${a.id})"><div class="card-cover"><div class="placeholder-icon">&#127908;</div></div><div class="card-info"><div class="card-title">${this.esc(a.name)}</div><div class="card-subtitle">${a.albumCount || 0} albums &middot; ${a.trackCount || 0} tracks</div></div></div>`; });
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
                const formatBadge = book.format === 'PDF' ? 'pdf' : 'epub';
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
            // Deduplicate by seriesName for TV shows
            const seen = new Set();
            const unique = data.videos.filter(v => {
                if (v.mediaType === 'tv' && v.seriesName) {
                    if (seen.has(v.seriesName)) return false;
                    seen.add(v.seriesName);
                }
                return true;
            });
            html += `<div class="section-title">${this.t('nav.moviesTvShows')}</div><div style="display:flex;flex-wrap:wrap;gap:14px;margin-bottom:20px">`;
            unique.forEach(v => {
                const poster = v.posterPath ? `/videometa/${v.posterPath}` : '';
                const title = v.seriesName || v.title;
                const click = v.mediaType === 'tv' && v.seriesName
                    ? `App.openSeriesDetail('${title.replace(/'/g, "\\'")}')`
                    : `App.openVideoDetail(${v.id})`;
                html += `<div style="width:130px;cursor:pointer;text-align:center" onclick="${click}">
                    <div style="width:130px;height:185px;border-radius:8px;overflow:hidden;background:var(--bg-card);margin-bottom:6px;border:1px solid var(--border-color)">
                        ${poster
                            ? `<img src="${poster}" style="width:100%;height:100%;object-fit:cover" loading="lazy" alt="">`
                            : `<div style="display:flex;align-items:center;justify-content:center;height:100%"><svg style="width:40px;height:40px;stroke:var(--text-secondary);fill:none;stroke-width:1.5"><use href="#icon-film"/></svg></div>`}
                    </div>
                    <div style="font-size:12px;font-weight:500;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${this.esc(title)}</div>
                    <div style="font-size:11px;color:var(--text-secondary)">${v.mediaType === 'tv' ? 'TV' : 'Movie'} ${v.year ? `(${v.year})` : ''}</div>
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

    emptyState(title, message) {
        return `<div class="empty-state"><div class="empty-icon">&#127925;</div><h2>${title}</h2><p>${message}</p></div>`;
    },

    // ── Video context menu & metadata editor ─────────────────────────

    _videoEditPosterData: null,

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
                <span class="video-menu-icon"><svg ${svgAttr}><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/></svg></span><span>Edit Metadata</span>
            </div>
            <div class="video-menu-item" onclick="App.refetchVideoMetadata(${videoId})">
                <span class="video-menu-icon"><svg ${svgAttr}><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg></span><span>Re-fetch from TMDB</span>
            </div>
            <div class="video-menu-divider"></div>
            <div class="video-menu-item" onclick="App.setVideoMediaType(${videoId}, 'movie')">
                <span class="video-menu-icon"><svg ${svgAttr}><rect x="2" y="2" width="20" height="20" rx="2.18" ry="2.18"/><line x1="7" y1="2" x2="7" y2="22"/><line x1="17" y1="2" x2="17" y2="22"/><line x1="2" y1="12" x2="22" y2="12"/><line x1="2" y1="7" x2="7" y2="7"/><line x1="2" y1="17" x2="7" y2="17"/><line x1="17" y1="7" x2="22" y2="7"/><line x1="17" y1="17" x2="22" y2="17"/></svg></span><span>Set as Movie</span>
                ${check('movie')}
            </div>
            <div class="video-menu-item" onclick="App.setVideoMediaType(${videoId}, 'tv')">
                <span class="video-menu-icon"><svg ${svgAttr}><rect x="2" y="7" width="20" height="15" rx="2" ry="2"/><polyline points="17 2 12 7 7 2"/></svg></span><span>Set as TV Show</span>
                ${check('tv')}
            </div>
            <div class="video-menu-item" onclick="App.setVideoMediaType(${videoId}, 'documentary')">
                <span class="video-menu-icon"><svg ${svgAttr}><circle cx="12" cy="12" r="10"/><polygon points="10 8 16 12 10 16 10 8"/></svg></span><span>Set as Documentary</span>
                ${check('documentary')}
            </div>
            <div class="video-menu-divider"></div>
            <div class="video-menu-item" onclick="App.toggleVideoWatched(${videoId})">
                <span class="video-menu-icon"><svg ${svgAttr}><polyline points="20 6 9 17 4 12"/></svg></span><span>${this._isVideoWatched(videoId) ? 'Mark as Unwatched' : 'Mark as Watched'}</span>
                ${this._isVideoWatched(videoId) ? '<span class="video-menu-check">&#10003;</span>' : ''}
            </div>
            <div class="video-menu-divider"></div>
            <div class="video-menu-item" onclick="App.openRatingPopup('video', ${videoId}, event)">
                <span class="video-menu-icon">&#9733;</span><span>Rate...</span>
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

    async setVideoMediaType(videoId, newType) {
        this.closeVideoMenu();
        const res = await fetch(`/api/videos/${videoId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ mediaType: newType })
        });
        if (res.ok) this.loadVideosPage();
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

    async openVideoEditModal(videoId) {
        this.closeVideoMenu();
        const v = await this.api(`videos/${videoId}`);
        if (!v) return;

        this._videoEditPosterData = null;

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
        overlay.addEventListener('click', (e) => { if (e.target === overlay) this.closeVideoEditModal(); });
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
                const typeLabel = v.mediaType === 'tv' ? 'TV' : v.mediaType === 'documentary' ? 'Doc' : 'Movie';
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
                const formatBadge = e.format === 'PDF' ? 'pdf' : 'epub';
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
                this.closeVideoEditModal();
                this.loadVideosPage();
            } else {
                alert('Error saving metadata: ' + (result.error || 'Unknown error'));
            }
        } catch (err) {
            alert('Error: ' + err.message);
        }
    }
};

document.addEventListener('DOMContentLoaded', () => App.init());
