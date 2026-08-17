import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Pango from 'gi://Pango';
import Soup from 'gi://Soup';
import St from 'gi://St';

import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as ModalDialog from 'resource:///org/gnome/shell/ui/modalDialog.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';

const UUID = 'YunXiStatistician';
const APP_VERSION = '1.4.4.2';
const LEADERBOARD_URL = 'https://kvdb.io/A2vqsiB5juK3mX6H9urPed';
const RELEASE_API_URL = 'https://api.github.com/repos/YunXi-0/YunXi/releases/latest';
const UPDATE_ASSET_NAME = 'YunXiStatistician-Linux-GNOME.zip';
const MAX_CONCURRENT_USER_FETCHES = 4;
const UPDATE_MIRRORS = [
    'https://mirror.ahuai.top/',
    'https://gh-proxy.com/',
    'https://ghfast.top/',
    'https://gh.ddlc.top/',
    'https://ghproxy.net/',
];
const THEMES = [
    ['经典', '#195ca7', false], ['深色', '#3b82f6', true], ['霜蓝', '#87d7ff', false],
    ['樱粉', '#ff96cd', false], ['薄荷', '#78ebbe', false], ['柠檬', '#ffe150', false],
    ['珊瑚', '#ff8269', false], ['靛青', '#5a78f0', false], ['葡萄', '#b46ef0', false],
    ['海盐', '#6ed2e6', false], ['蜜桃', '#ffb98c', false], ['青柠', '#afe65a', false],
    ['玫瑰', '#f55f8c', false], ['天蓝', '#50b9fa', false], ['暖阳', '#ffc341', false],
    ['紫藤', '#aa78eb', false], ['抹茶', '#96cd64', false], ['赤金', '#eb9b2d', false],
    ['冰晶', '#96ebf5', false], ['梅子', '#d75aaf', false], ['湖绿', '#46b9a5', false],
    ['琥珀', '#ffaa46', false],
];
const COLLECTION_ARTS = [
    ['diamond', '#46beff', '#195f91', '#ebfcff'], ['diamond', '#ff7dbe', '#af236e', '#fff0fa'],
    ['diamond', '#6edc91', '#1e7846', '#ebfff0'], ['diamond', '#ffaf3c', '#be5f19', '#fff5dc'],
    ['diamond', '#be82ff', '#6e32b4', '#f5ebff'],
    ['candy', '#ff6091', '#d2234b', '#fff5f8'], ['candy', '#55b4ff', '#235aaf', '#f5fcff'],
    ['candy', '#c878ff', '#7d2dbe', '#faf5ff'], ['candy', '#ffaf37', '#cd5f19', '#fffaeb'],
    ['candy', '#82dc78', '#2d823c', '#f5fff0'],
    ['crystal', '#9b5fe1', '#502887', '#e1cdff'], ['crystal', '#5abeff', '#235aaf', '#e1f5ff'],
    ['crystal', '#ff6ed2', '#af237d', '#ffe6f8'], ['crystal', '#ffc346', '#be6919', '#fff8dc'],
    ['crystal', '#73e1be', '#19825f', '#e6fff5'],
    ['pumpkin', '#ff8f28', '#c35519', '#509137'], ['pumpkin', '#87cd5a', '#377d2d', '#dcaf3c'],
    ['pumpkin', '#b478ff', '#6932b4', '#46b46e'], ['pumpkin', '#5aafff', '#235aaf', '#4baf5f'],
    ['pumpkin', '#f55f5f', '#a52332', '#559b3c'],
    ['emerald', '#37b4d2', '#196991', '#dcf5ff'], ['emerald', '#9155d7', '#461e82', '#ebd7ff'],
    ['emerald', '#3cb978', '#0f6941', '#d7ffeb'], ['emerald', '#e1465f', '#87142d', '#ffCDD7'],
    ['emerald', '#fab93c', '#af5f14', '#fff5cd'],
    ['round', '#37b4d2', '#196991', '#dcf5ff'], ['round', '#9155d7', '#461e82', '#ebd7ff'],
    ['round', '#3cb978', '#0f6941', '#d7ffeb'], ['round', '#e1465f', '#87142d', '#ffCDD7'],
    ['round', '#fab93c', '#af5f14', '#fff5cd'],
    ['teardrop', '#37b4d2', '#196991', '#dcf5ff'], ['teardrop', '#9155d7', '#461e82', '#ebd7ff'],
    ['teardrop', '#3cb978', '#0f6941', '#d7ffeb'], ['teardrop', '#e1465f', '#87142d', '#ffCDD7'],
    ['teardrop', '#fab93c', '#af5f14', '#fff5cd'],
    ['heart', '#37b4d2', '#196991', '#dcf5ff'], ['heart', '#9155d7', '#461e82', '#ebd7ff'],
    ['heart', '#3cb978', '#0f6941', '#d7ffeb'], ['heart', '#e1465f', '#87142d', '#ffCDD7'],
    ['heart', '#fab93c', '#af5f14', '#fff5cd'],
];

function dayKey(date = new Date()) {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
}

function sanitizeId(value) {
    return [...String(value ?? '')]
        .filter(char => /[A-Za-z0-9\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]/.test(char))
        .slice(0, 10)
        .join('') || 'USER';
}

function emptyDay() {
    return {powered: 0, awake: 0, active: 0, left: 0, right: 0, keys: 0,
        wasd: 0, qwer: 0, shift: 0, ctrl: 0, tab: 0, space: 0,
        backspace: 0, enter: 0, arrows: 0, maxCps: 0, maxKps: 0,
        maxAps: 0, app: 0, qq: 0, wechat: 0, mouseIdle: 0,
        edge: 0, corner: 0, center: 0, longestUptime: 0};
}

class StatisticsStore {
    constructor() {
        this.directory = GLib.build_filenamev([GLib.get_user_data_dir(), 'yunxi']);
        this.path = GLib.build_filenamev([this.directory, 'gnome-statistics.json']);
        this.data = {version: 1, config: {x: 24, y: 80, locked: false, hidden: false,
            scale: 1, theme: 0, dataView: 0, statsView: 0, chartKind: 0, period: 7, name: sanitizeId(GLib.get_user_name()),
            snapToEdge: false,
            luckDate: '', luck: null, collections: 0, timerEnd: 0}, days: {}};
        this.load();
    }

    load() {
        try {
            const [ok, bytes] = GLib.file_get_contents(this.path);
            if (!ok)
                return;
            const loaded = JSON.parse(new TextDecoder().decode(bytes));
            this.data.config = {...this.data.config, ...(loaded.config ?? {})};
            this.data.days = loaded.days ?? {};
        } catch (_) {
        }
    }

    day(key = dayKey()) {
        this.data.days[key] = {...emptyDay(), ...(this.data.days[key] ?? {})};
        return this.data.days[key];
    }

    save() {
        try {
            GLib.mkdir_with_parents(this.directory, 0o700);
            const temp = `${this.path}.tmp`;
            GLib.file_set_contents(temp, JSON.stringify(this.data));
            GLib.rename(temp, this.path);
        } catch (_) {
        }
    }
}

export default class YunXiExtension extends Extension {
    enable() {
        this._enabled = true;
        this._store = new StatisticsStore();
        this._store.data.config.hidden = false;
        this._store.save();
        this._history = [];
        this._page = 'data';
        this._lastTick = GLib.get_monotonic_time();
        this._lastInput = this._lastTick;
        this._lastMouse = this._lastTick;
        this._bucket = Math.floor(Date.now() / 5000);
        this._bucketInput = false;
        this._rateSecond = 0;
        this._secondClicks = 0;
        this._secondKeys = 0;
        this._centerStreak = 0;
        this._pressed = new Set();
        this._collectionMinute = -1;
        this._collectionVisible = false;
        this._leaderboardMetric = 'active';
        this._leaderboardPeriod = 1;
        this._leaderboardEntries = [];
        this._leaderboardStatus = '全部排行榜已同步';
        this._settingsStatus = '当前已是最新版本';
        this._httpSession = new Soup.Session({timeout: 15});
        this._buildIndicator();
        this._buildWidget();
        this._buildContextMenu();
        this._eventId = global.stage.connect('captured-event', (_actor, event) => this._capture(event));
        this._timerId = GLib.timeout_add_seconds(GLib.PRIORITY_DEFAULT, 1, () => this._tick());
        this._saveId = GLib.timeout_add_seconds(GLib.PRIORITY_DEFAULT, 30, () => {
            this._store.save();
            return GLib.SOURCE_CONTINUE;
        });
        this._readPowerHistory();
        const remainingTimer = Math.ceil((this._store.data.config.timerEnd - Date.now()) / 1000);
        if (remainingTimer > 0)
            this._startTimerSeconds(remainingTimer);
        this._tick();
        this._startupUpdateId = GLib.timeout_add_seconds(GLib.PRIORITY_DEFAULT, 8, () => {
            this._checkForUpdates(false);
            this._startupUpdateId = 0;
            return GLib.SOURCE_REMOVE;
        });
        this._watchUpdateResult();
    }

    disable() {
        this._enabled = false;
        if (this._eventId)
            global.stage.disconnect(this._eventId);
        for (const id of [this._timerId, this._saveId, this._snapId, this._startupUpdateId,
            this._updateResultId]) {
            if (id)
                GLib.source_remove(id);
        }
        this._store?.save();
        this._httpSession?.abort();
        if (this._dragStageId)
            global.stage.disconnect(this._dragStageId);
        if (this._resizeStageId)
            global.stage.disconnect(this._resizeStageId);
        this._dragState = null;
        this._resizeState = null;
        this._contextMenu?.destroy();
        this._collectionActor?.destroy();
        this._timerBubble?.destroy();
        this._root?.destroy();
        this._indicator?.destroy();
        this._root = this._indicator = this._store = this._httpSession = null;
    }

    _buildIndicator() {
        this._indicator = new PanelMenu.Button(0.0, '云曦 PC 统计', false);
        this._indicator.add_child(new St.Label({text: '云', y_align: Clutter.ActorAlign.CENTER}));
        this._indicator.menu.addAction('打开界面', () => this._setVisible(true));
        this._indicator.menu.addAction('退出', () => this._disableSelf());
        Main.panel.addToStatusArea('yunxi', this._indicator);
    }

    _buildWidget() {
        this._root = new St.Widget({reactive: true, width: 200, height: 200});
        this._panel = new St.BoxLayout({vertical: true, width: 200, height: 200});
        this._root.add_child(this._panel);
        this._root.set_position(this._store.data.config.x, this._store.data.config.y);
        Main.layoutManager.addChrome(this._root, {trackFullscreen: true});
        this._root.connect('button-press-event', (_actor, event) => {
            if (event.get_button() === 3) {
                this._contextMenu?.toggle();
                return Clutter.EVENT_STOP;
            }
            return Clutter.EVENT_PROPAGATE;
        });
        this._root.connect('enter-event', () => {
            if (this._snapRestoreX !== undefined)
                this._root.set_x(this._snapRestoreX);
        });
        this._root.connect('leave-event', () => {
            if (this._snapRestoreX === undefined)
                return;
            if (this._snapId) GLib.source_remove(this._snapId);
            this._snapId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 700, () => {
                this._root?.set_x(this._snapHiddenX);
                this._snapId = 0;
                return GLib.SOURCE_REMOVE;
            });
        });
        this._render();
    }

    _beginDrag(event) {
        if (event.get_button() !== 1 || this._store.data.config.locked || this._dragState || this._resizeState)
            return Clutter.EVENT_PROPAGATE;
        const [pointerX, pointerY] = global.get_pointer();
        this._dragState = {pointerX, pointerY, x: this._root.x, y: this._root.y};
        this._dragStageId = global.stage.connect('captured-event', (_actor, dragEvent) =>
            this._handleDragEvent(dragEvent));
        return Clutter.EVENT_STOP;
    }

    _makeDragSource(actor) {
        actor.reactive = true;
        actor.connect('button-press-event', (_actor, event) => this._beginDrag(event));
        return actor;
    }

    _addDragPad(parent, width, height) {
        if (this._store.data.config.locked)
            return;
        const pad = new St.Widget({reactive: true, width, height});
        pad.connect('button-press-event', (_actor, event) => this._beginDrag(event));
        parent.add_child(pad);
    }

    _handleDragEvent(event) {
        if (!this._dragState)
            return Clutter.EVENT_PROPAGATE;
        if (event.type() === Clutter.EventType.MOTION) {
            const [pointerX, pointerY] = global.get_pointer();
            this._root.set_position(
                this._dragState.x + pointerX - this._dragState.pointerX,
                this._dragState.y + pointerY - this._dragState.pointerY);
            this._placeTimerBubble();
            return Clutter.EVENT_STOP;
        }
        if (event.type() === Clutter.EventType.BUTTON_RELEASE && event.get_button() === 1) {
            global.stage.disconnect(this._dragStageId);
            this._dragStageId = 0;
            this._dragState = null;
            this._store.data.config.x = Math.round(this._root.x);
            this._store.data.config.y = Math.round(this._root.y);
            if (this._store.data.config.snapToEdge)
                this._snapToNearestEdge();
            this._placeTimerBubble();
            this._store.save();
            return Clutter.EVENT_PROPAGATE;
        }
        return Clutter.EVENT_PROPAGATE;
    }

    _resizeEdgeAt(x, y) {
        if (!this._root?.visible || this._store.data.config.locked)
            return '';
        const [left, top] = this._root.get_transformed_position();
        const [width, height] = this._root.get_transformed_size();
        const margin = Math.max(6, Math.round(6 * this._store.data.config.scale));
        if (x < left || x > left + width || y < top || y > top + height)
            return '';
        const horizontal = x - left <= margin ? 'w' : left + width - x <= margin ? 'e' : '';
        const vertical = y - top <= margin ? 'n' : top + height - y <= margin ? 's' : '';
        return vertical + horizontal;
    }

    _beginResize(event, edge) {
        if (!edge || this._dragState || this._resizeState)
            return Clutter.EVENT_PROPAGATE;
        const [pointerX, pointerY] = event.get_coords();
        const [left, top] = this._root.get_transformed_position();
        const [width, height] = this._root.get_transformed_size();
        this._resizeState = {
            edge, pointerX, pointerY, left, top, width, height,
            right: left + width, bottom: top + height,
            baseWidth: this._root.width, baseHeight: this._root.height,
            scale: this._store.data.config.scale,
        };
        this._resizeStageId = global.stage.connect('captured-event', (_actor, resizeEvent) =>
            this._handleResizeEvent(resizeEvent));
        return Clutter.EVENT_STOP;
    }

    _handleResizeEvent(event) {
        const state = this._resizeState;
        if (!state)
            return Clutter.EVENT_PROPAGATE;
        if (event.type() === Clutter.EventType.MOTION) {
            const [x, y] = event.get_coords();
            const factors = [];
            if (state.edge.includes('e')) factors.push((x - state.left) / state.width);
            if (state.edge.includes('w')) factors.push((state.right - x) / state.width);
            if (state.edge.includes('s')) factors.push((y - state.top) / state.height);
            if (state.edge.includes('n')) factors.push((state.bottom - y) / state.height);
            const factor = factors.reduce((selected, value) =>
                Math.abs(value - 1) > Math.abs(selected - 1) ? value : selected, factors[0] ?? 1);
            const scale = Math.max(0.75, Math.min(2, state.scale * factor));
            const rootX = state.edge.includes('w') ? state.right - state.baseWidth * scale : state.left;
            const rootY = state.edge.includes('n') ? state.bottom - state.baseHeight * scale : state.top;
            this._store.data.config.scale = scale;
            this._root.set_scale(scale, scale);
            this._root.set_position(Math.round(rootX), Math.round(rootY));
            this._placeTimerBubble();
            return Clutter.EVENT_STOP;
        }
        if (event.type() === Clutter.EventType.BUTTON_RELEASE && event.get_button() === 1) {
            global.stage.disconnect(this._resizeStageId);
            this._resizeStageId = 0;
            this._resizeState = null;
            this._store.data.config.x = Math.round(this._root.x);
            this._store.data.config.y = Math.round(this._root.y);
            if (this._store.data.config.snapToEdge)
                this._snapToNearestEdge();
            this._store.save();
            return Clutter.EVENT_STOP;
        }
        return Clutter.EVENT_PROPAGATE;
    }

    _snapToNearestEdge() {
        const scale = this._store.data.config.scale;
        const width = this._root.width * scale;
        const monitor = Main.layoutManager.monitors.find(item =>
            this._root.x + width / 2 >= item.x &&
            this._root.x + width / 2 < item.x + item.width) ?? Main.layoutManager.primaryMonitor;
        if (!monitor)
            return;
        const leftDistance = Math.abs(this._root.x - monitor.x);
        const rightDistance = Math.abs(monitor.x + monitor.width - (this._root.x + width));
        this._snapRestoreX = leftDistance <= rightDistance
            ? monitor.x : monitor.x + monitor.width - width;
        this._snapHiddenX = leftDistance <= rightDistance
            ? monitor.x - width + 5 : monitor.x + monitor.width - 5;
        this._root.set_x(this._snapHiddenX);
    }

    _buildContextMenu() {
        this._contextMenu = new PopupMenu.PopupMenu(this._root, 0.0, St.Side.TOP);
        Main.uiGroup.add_child(this._contextMenu.actor);
        this._contextMenu.actor.hide();
        this._contextMenu.addAction('退出', () => this._disableSelf());
    }

    _render() {
        if (!this._panel)
            return;
        for (const child of this._panel.get_children())
            child.destroy();
        const config = this._store.data.config;
        config.scale = Math.max(0.75, Math.min(2, Number(config.scale) || 1));
        this._root.set_scale(config.scale, config.scale);
        this._root.reactive = !config.locked;
        this._lockActor = null;
        const theme = THEMES[Math.max(0, Math.min(THEMES.length - 1, config.theme))];
        this._panel.style_class = `yunxi-widget ${theme[2] ? 'dark' : ''} ${config.locked ? 'locked' : ''}`;
        this._panel.set_style(config.locked
            ? 'background-color: transparent; border-color: transparent; box-shadow: none; color: white;'
            : `border-color: ${theme[1]}; ${theme[2] ? 'background-color: rgba(24,27,33,0.96); color: #e2e8f0;' : ''}`);
        const expanded = this._page === 'stats' || this._page === 'leaderboard';
        this._root.set_size(expanded ? 400 : 200, expanded ? 360 : 200);
        this._panel.set_size(expanded ? 400 : 200, expanded ? 360 : 200);
        this._titleLabel = null;
        if (this._page !== 'stats') {
            this._titleLabel = new St.Button({
                label: this._pageTitle(),
                style_class: 'yunxi-title yunxi-drag-handle',
                reactive: !config.locked,
                can_focus: false,
            });
            if (!config.locked)
                this._makeDragSource(this._titleLabel);
            this._titleLabel.set_style(`color: ${theme[1]};`);
            this._titleLabel.x_align = Clutter.ActorAlign.CENTER;
            this._panel.add_child(this._titleLabel);
        }
        this._content = new St.BoxLayout({vertical: true, x_expand: true, y_expand: true});
        this._panel.add_child(this._content);
        if (this._page === 'data') this._renderData();
        else if (this._page === 'stats') this._renderStats();
        else if (this._page === 'leaderboard') this._renderLeaderboard();
        else if (this._page === 'performance') this._renderPerformance();
        else this._renderSettings();
        if (!config.locked)
            this._renderNavigation();
        this._root.visible = !config.hidden;
    }

    _renderData() {
        const day = this._store.day();
        const view = this._store.data.config.dataView;
        const surface = new St.Widget({
            layout_manager: new Clutter.FixedLayout(),
            x_expand: true,
            y_expand: true,
        });
        this._addDragPad(surface, 184, 140);
        const metrics = new St.BoxLayout({vertical: true, width: 152, height: 140});
        if (view === 0) {
            const uptime = this._uptime();
            this._metric('运行时间', this._format(Math.max(day.powered, this._poweredToday(uptime))), metrics);
            this._metric('非睡眠时间', this._format(day.awake), metrics);
            this._metric('高强度使用', this._format(day.active), metrics);
        } else if (view === 1) {
            [`总点击：${this._number(day.left + day.right)}`, `左键：${this._number(day.left)}`,
                `右键：${this._number(day.right)}`, `键盘：${this._number(day.keys)}`]
                .forEach(text => metrics.add_child(this._dragLabel(text, 'yunxi-input-value')));
        } else {
            this._metric('当日最大 CPS', `${day.maxCps} 次/秒`, metrics);
            this._metric('当日最大 KPS', `${day.maxKps} 次/秒`, metrics);
            this._metric('当日最大 APS', `${day.maxAps} 次/秒`, metrics);
        }
        const toggleLock = () => {
            this._store.data.config.locked = !this._store.data.config.locked;
            this._store.save();
            this._render();
        };
        metrics.set_position(0, 0);
        surface.add_child(metrics);
        if (this._store.data.config.locked) {
            const lock = this._button('锁', toggleLock, true);
            lock.set_position(156, 114);
            lock.set_size(24, 24);
            surface.add_child(lock);
            this._lockActor = lock;
        } else {
            const rail = new St.BoxLayout({vertical: true, width: 24, height: 104});
            for (let i = 0; i < 3; i++) rail.add_child(this._button(String(i + 1), () => {
                this._store.data.config.dataView = i; this._render();
            }, i === view));
            rail.add_child(this._button('锁', toggleLock));
            rail.set_position(156, 42);
            surface.add_child(rail);
        }
        this._content.add_child(surface);
    }

    _renderStats() {
        const config = this._store.data.config;
        const controls = new St.BoxLayout({x_expand: true});
        [[7, '7天'], [30, '30'], [90, '90']].forEach(([days, label]) => controls.add_child(this._button(label, () => {
            config.period = days; this._render();
        }, config.period === days)));
        controls.add_child(new St.Widget({x_expand: true}));
        controls.add_child(this._button('全部', () => this._showAllStatistics()));
        this._content.add_child(controls);
        const surface = new St.Widget({layout_manager: new Clutter.FixedLayout(), x_expand: true, y_expand: true});
        this._addDragPad(surface, 380, 282);
        const body = new St.BoxLayout({vertical: true, width: 350, height: 282});
        const kinds = config.statsView === 0
            ? [['总', 0], ['运', 1], ['非', 2], ['高', 3]]
            : [['总', 0], ['左', 1], ['右', 2], ['键', 3]];
        const kindRow = new St.BoxLayout({x_expand: true});
        kinds.forEach(([label, index]) => kindRow.add_child(this._button(label, () => {
            config.chartKind = index; this._render();
        }, config.chartKind === index)));
        body.add_child(kindRow);
        const rows = this._recent(config.period);
        const values = rows.map((day, index) => {
            const date = new Date(); date.setHours(0, 0, 0, 0); date.setDate(date.getDate() - (rows.length - index - 1));
            return {date, powered: day.powered, awake: day.awake, active: day.active,
                mouseTotal: day.left + day.right, mouseLeft: day.left, mouseRight: day.right, keyboard: day.keys};
        });
        const chart = new St.DrawingArea({style_class: 'yunxi-chart', x_expand: true, y_expand: true});
        chart.connect('repaint', area => this._paintChart(area, values, config.statsView, config.chartKind, config.period));
        body.add_child(chart);
        const viewRail = new St.BoxLayout({vertical: true, width: 24, height: 52});
        viewRail.add_child(this._button('1', () => {
            config.statsView = 0; config.chartKind = 0; this._render();
        }, config.statsView === 0));
        viewRail.add_child(this._button('2', () => {
            config.statsView = 1; config.chartKind = 0; this._render();
        }, config.statsView === 1));
        body.set_position(0, 0);
        viewRail.set_position(356, 80);
        surface.add_child(body);
        surface.add_child(viewRail);
        this._content.add_child(surface);
    }

    _renderLeaderboard() {
        const config = this._store.data.config;
        const canvas = new St.Widget({layout_manager: new Clutter.FixedLayout(), x_expand: true, y_expand: true});
        this._addDragPad(canvas, 380, 282);
        const place = (actor, x, y, width, height) => {
            actor.set_position(x, y);
            actor.set_size(width, height);
            canvas.add_child(actor);
            return actor;
        };
        place(this._button('刷新', () => this._refreshLeaderboard()), 0, 0, 80, 28);
        place(this._dragLabel(`UUid：${config.deviceUuid ?? '--'}`, 'yunxi-uuid'), 126, 4, 132, 20);
        place(this._button('修改ID', () => this._showEditIdDialog()), 286, 0, 90, 28);
        [['高强度', 'active'], ['总点击', 'mouse_total'], ['左键', 'mouse_left'],
            ['右键', 'mouse_right'], ['键盘', 'keyboard'], ['运气', 'luck'], ['藏品', 'collections']]
            .forEach(([label, metric], index) => place(this._button(label, () => {
                this._leaderboardMetric = metric;
                if (metric === 'luck') this._leaderboardPeriod = 1;
                if (metric === 'collections') this._leaderboardPeriod = 0;
                this._buildLeaderboardEntries(); this._render();
            }, metric === this._leaderboardMetric), index * 52, 42, 50, 24));
        if (!['luck', 'collections'].includes(this._leaderboardMetric)) {
            [[1, '1'], [7, '7'], [30, '30'], [0, '总']].forEach(([period, label], index) =>
                place(this._button(label, () => {
                    this._leaderboardPeriod = period; this._buildLeaderboardEntries(); this._render();
                }, period === this._leaderboardPeriod), 356, 92 + index * 30, 24, 24));
        }
        const luckBlank = this._leaderboardMetric === 'luck' && config.luckDate !== dayKey();
        const collectionBlank = this._leaderboardMetric === 'collections' && config.collections === 0;
        if (luckBlank) {
            place(this._button('抽取今日运气值', () => {
                config.luckDate = dayKey(); config.luck = Math.floor(Math.random() * 101);
                this._store.save(); this._showLuck(config.luck); this._refreshLeaderboard(); this._render();
            }), 122, 134, 140, 32);
        } else if (collectionBlank) {
            place(this._dragLabel('你必须先找到至少一个藏品', 'yunxi-empty-message'), 22, 126, 340, 36);
        } else {
            place(this._dragLabel(this._leaderboardStatus, 'yunxi-leaderboard-status'), 0, 252, 300, 20);
            for (let rank = 0; rank < 5; rank++) {
                const entry = this._leaderboardEntries[rank];
                const value = entry ? this._formatLeaderboardValue(entry.value) : '';
                place(this._dragLabel(entry
                    ? `${rank + 1}. ${entry.name}  ${value}` : `${rank + 1}. 暂无`, 'yunxi-leaderboard-entry'),
                0, 78 + rank * 34, 330, 32);
            }
            place(this._button('全部', () => this._showAllLeaderboard()), 314, 248, 56, 24);
        }
        this._content.add_child(canvas);
    }

    _renderPerformance() {
        const process = this._readProcessMetrics();
        const canvas = new St.Widget({layout_manager: new Clutter.FixedLayout(), x_expand: true, y_expand: true});
        this._addDragPad(canvas, 184, 142);
        const rows = [
            ['CPU', `相对：${process.cpuPercent.toFixed(1)}%\n绝对：${this._formatFrequency(process.cpuHz)}`],
            ['进程资源', `线程：${process.threads}\n句柄：${process.handles}`],
            ['GNOME Shell 内存', `总占用：${process.memoryPercent.toFixed(1)}%\nRSS：${this._formatMemory(process.memoryMb)}`],
        ];
        rows.forEach(([name, value], index) => {
            const top = index * 48;
            const nameLabel = this._dragLabel(name, 'yunxi-name');
            const valueLabel = this._dragLabel(value, 'yunxi-perf-value');
            nameLabel.set_position(6, top);
            nameLabel.set_size(156, 16);
            valueLabel.set_position(6, top + 17);
            valueLabel.set_size(156, 30);
            canvas.add_child(nameLabel);
            canvas.add_child(valueLabel);
        });
        this._content.add_child(canvas);
    }

    _renderSettings() {
        this._content.add_child(this._button('隐藏主界面', () => this._setVisible(false)));
        this._content.add_child(this._button('更新日志', () => this._showText('更新日志', this._readChangelog())));
        const actions = new St.BoxLayout({x_align: Clutter.ActorAlign.CENTER});
        actions.add_child(this._button('功能', () => this._showFeatures()));
        actions.add_child(this._button('检测最新', () => this._checkForUpdates()));
        actions.add_child(this._button('关于', () => this._showAbout()));
        this._content.add_child(actions);
        this._content.add_child(this._dragLabel(this._settingsStatus, 'yunxi-small'));
        this._content.add_child(this._dragLabel(`当前版本：${APP_VERSION}`, 'yunxi-version'));
    }

    _showAbout() {
        const match = this._readChangelog().match(
            /^版本\s+\d+(?:\.\d+)*（(\d{2})(\d{2})(\d{2})\s+(\d{2}:\d{2})）/m);
        const releaseDate = match
            ? `20${match[1]}-${match[2]}-${match[3]} ${match[4]}`
            : '未知';
        this._showText('关于', [
            '软件名称：云曦PC统计',
            `版本号：${APP_VERSION}`,
            `更新日期：${releaseDate}`,
            '开发人员：Yun_Xi  ahuai',
            'git地址：https://github.com/YunXi-0/YunXi',
        ].join('\n'));
    }

    _showFeatures() {
        const config = this._store.data.config;
        const dialog = new ModalDialog.ModalDialog();
        dialog.contentLayout.add_child(this._label('功能设置', 'yunxi-title'));
        const snap = this._button('贴边自动隐藏', () => {
            config.snapToEdge = !config.snapToEdge;
            if (config.snapToEdge) snap.add_style_pseudo_class('selected');
            else snap.remove_style_pseudo_class('selected');
            if (!config.snapToEdge && this._snapRestoreX !== undefined) {
                this._root.set_x(this._snapRestoreX);
                this._snapRestoreX = this._snapHiddenX = undefined;
            } else if (config.snapToEdge) {
                this._snapToNearestEdge();
            }
            this._store.save();
        }, config.snapToEdge);
        dialog.contentLayout.add_child(snap);
        const topMost = this._button('组件置顶', () => {}, true);
        topMost.reactive = false;
        dialog.contentLayout.add_child(topMost);
        dialog.contentLayout.add_child(this._button('恢复默认尺寸', () => {
            config.x = 24; config.y = 80; config.scale = 1;
            this._root.set_scale(1, 1);
            this._root.set_position(config.x, config.y);
            this._snapRestoreX = this._snapHiddenX = undefined;
            this._placeTimerBubble();
            this._store.save();
        }));
        dialog.contentLayout.add_child(this._button('切换主题', () => {
            dialog.close(); this._showThemePicker();
        }));
        dialog.contentLayout.add_child(this._button('计时器', () => {
            dialog.close(); this._showTimerConfig();
        }));
        dialog.setButtons([{label: '关闭', action: () => dialog.close(), key: Clutter.KEY_Escape}]);
        dialog.open();
    }

    _showThemePicker() {
        const dialog = new ModalDialog.ModalDialog();
        dialog.contentLayout.add_child(this._label('选择主题', 'yunxi-title'));
        const scroll = new St.ScrollView({style_class: 'yunxi-theme-list', overlay_scrollbars: true});
        const list = new St.BoxLayout({vertical: true});
        THEMES.forEach(([name, color], index) => {
            const button = this._button(`${index === this._store.data.config.theme ? '● ' : ''}${name}`, () => {
                this._store.data.config.theme = index; this._store.save(); this._render(); dialog.close();
            });
            button.set_style(`color: ${color};`);
            list.add_child(button);
        });
        scroll.set_child(list);
        dialog.contentLayout.add_child(scroll);
        dialog.setButtons([{label: '取消', action: () => dialog.close(), key: Clutter.KEY_Escape}]);
        dialog.open();
    }

    _showTimerConfig() {
        const dialog = new ModalDialog.ModalDialog();
        dialog.contentLayout.add_child(this._label('计时器', 'yunxi-title'));
        const row = new St.BoxLayout({x_align: Clutter.ActorAlign.CENTER});
        const values = [['0', '时'], ['5', '分'], ['0', '秒']].map(([value, unit]) => {
            const entry = new St.Entry({text: value, style_class: 'yunxi-time-entry'});
            row.add_child(entry); row.add_child(this._label(unit, 'yunxi-small'));
            return entry;
        });
        dialog.contentLayout.add_child(row);
        dialog.setButtons([
            {label: '确定', action: () => {
                const numbers = values.map(entry => Math.max(0, Number.parseInt(entry.get_text(), 10) || 0));
                const seconds = numbers[0] * 3600 + numbers[1] * 60 + numbers[2];
                this._startTimerSeconds(seconds); dialog.close();
            }},
            {label: '取消', action: () => dialog.close(), key: Clutter.KEY_Escape},
        ]);
        dialog.open();
    }

    _showEditIdDialog() {
        const dialog = new ModalDialog.ModalDialog();
        dialog.contentLayout.add_child(this._label('修改用户ID', 'yunxi-title'));
        dialog.contentLayout.add_child(this._label('请输入用户ID（中英文或数字，最多10位）', 'yunxi-small'));
        const entry = new St.Entry({text: this._store.data.config.name, style_class: 'yunxi-id-entry'});
        dialog.contentLayout.add_child(entry);
        dialog.setButtons([
            {label: '确定', action: () => {
                this._store.data.config.name = sanitizeId(entry.get_text());
                this._store.save(); this._render(); dialog.close();
            }},
            {label: '取消', action: () => dialog.close(), key: Clutter.KEY_Escape},
        ]);
        dialog.open();
    }

    _showAllLeaderboard() {
        const title = this._leaderboardMetric === 'active' ? '高强度' :
            ({mouse_total: '总点击', mouse_left: '左键', mouse_right: '右键', keyboard: '键盘', luck: '运气', collections: '藏品'}[this._leaderboardMetric] ?? '排行榜');
        const lines = this._leaderboardEntries.length
            ? this._leaderboardEntries.map((entry, index) => `${index + 1}. ${entry.name}  ${this._formatLeaderboardValue(entry.value)}`)
            : ['暂无'];
        this._showText(`全部 · ${title}`, lines.join('\n'));
    }

    _showLuck(value) {
        const dialog = new ModalDialog.ModalDialog();
        const content = new St.BoxLayout({vertical: true, style_class: 'yunxi-luck-dialog'});
        content.add_child(this._label('今日运气值', 'yunxi-title'));
        content.add_child(this._label(String(value), 'yunxi-luck-value'));
        content.add_child(this._label('已为你锁定，次日零点重置', 'yunxi-small'));
        dialog.contentLayout.add_child(content);
        dialog.setButtons([{label: '确定', action: () => dialog.close(), key: Clutter.KEY_Escape}]);
        dialog.open();
    }

    _showTimerDone() {
        const dialog = new ModalDialog.ModalDialog();
        const content = new St.BoxLayout({vertical: true, style_class: 'yunxi-timer-done'});
        const message = this._label('计时器时间到', 'yunxi-timer-done-message');
        message.x_align = Clutter.ActorAlign.CENTER;
        message.y_align = Clutter.ActorAlign.CENTER;
        message.x_expand = true;
        message.y_expand = true;
        content.add_child(message);
        dialog.contentLayout.add_child(content);
        dialog.setButtons([{label: '确定', action: () => dialog.close(), key: Clutter.KEY_Escape}]);
        dialog.open();
    }

    async _refreshLeaderboard() {
        if (this._leaderboardLoading)
            return;
        this._leaderboardLoading = true;
        this._leaderboardStatus = '正在同步排行榜...';
        this._render();
        try {
            const registry = await this._getKvdb('registry') ?? {uuid_counter: 0, uuid_map: {}};
            const uuid = await this._ensureDeviceUuid(registry);
            await this._submitLeaderboard(uuid);
            const uuids = [...new Set(Object.values(registry.uuid_map ?? {}))];
            if (!uuids.includes(uuid))
                uuids.push(uuid);
            const records = await this._loadLeaderboardRecords(uuids);
            this._leaderboardRecords = records.filter(([, data]) => data).map(([id, data]) => ({id, data}));
            this._buildLeaderboardEntries();
            this._leaderboardStatus = '排行榜已同步';
        } catch (error) {
            this._leaderboardStatus = `排行榜同步失败：${error.message ?? error}`;
        } finally {
            this._leaderboardLoading = false;
            if (this._page === 'leaderboard')
                this._render();
        }
    }

    async _loadLeaderboardRecords(uuids) {
        const records = new Array(uuids.length);
        let nextIndex = 0;
        const worker = async () => {
            while (nextIndex < uuids.length) {
                const index = nextIndex++;
                const id = uuids[index];
                try {
                    records[index] = [id, await this._getKvdb(`user_${id}`)];
                } catch (_) {
                    records[index] = [id, null];
                }
            }
        };
        const workerCount = Math.min(MAX_CONCURRENT_USER_FETCHES, uuids.length);
        await Promise.all(Array.from({length: workerCount}, () => worker()));
        return records;
    }

    async _ensureDeviceUuid(registry) {
        const config = this._store.data.config;
        if (config.deviceUuid)
            return config.deviceUuid;
        let machineId = GLib.get_host_name();
        try {
            const [ok, bytes] = GLib.file_get_contents('/etc/machine-id');
            if (ok) machineId = new TextDecoder().decode(bytes).trim();
        } catch (_) {}
        const fingerprint = GLib.compute_checksum_for_string(
            GLib.ChecksumType.SHA256, `CloudXiPcStatistician:v1:${machineId}`, -1);
        registry.uuid_map ??= {};
        let uuid = registry.uuid_map[fingerprint];
        if (!uuid) {
            const counter = Number(registry.uuid_counter ?? 0);
            uuid = String(counter).padStart(3, '0');
            registry.uuid_map[fingerprint] = uuid;
            registry.uuid_counter = counter + 1;
            await this._putKvdb('registry', registry);
        }
        config.deviceUuid = uuid;
        this._store.save();
        return uuid;
    }

    async _submitLeaderboard(uuid) {
        const key = `user_${uuid}`;
        const config = this._store.data.config;
        const current = await this._getKvdb(key) ?? {
            schema_version: 2, uuid, name: config.name, totals: {}, entries: {},
        };
        current.schema_version = 2;
        current.uuid = uuid;
        current.name = config.name;
        current.totals ??= {};
        current.entries ??= {};
        const keyToday = dayKey();
        const day = this._store.day();
        const values = {
            active: day.active,
            mouse_total: day.left + day.right,
            mouse_left: day.left,
            mouse_right: day.right,
            keyboard: day.keys,
        };
        if (config.luckDate === keyToday) values.luck = config.luck;
        values.collections = config.collections;
        const oldDay = current.entries[keyToday] ?? {};
        for (const metric of ['active', 'mouse_total', 'mouse_left', 'mouse_right', 'keyboard']) {
            const oldValue = Number(oldDay[metric]?.[0]?.value ?? 0);
            current.totals[metric] = Number(current.totals[metric] ?? 0) + values[metric] - oldValue;
        }
        current.entries[keyToday] = oldDay;
        for (const [metric, value] of Object.entries(values))
            oldDay[metric] = [{uuid, name: config.name, value}];
        const cutoff = new Date(); cutoff.setDate(cutoff.getDate() - 29);
        for (const keyDate of Object.keys(current.entries)) {
            if (/^\d{4}-\d{2}-\d{2}$/.test(keyDate) && new Date(`${keyDate}T00:00:00`) < cutoff)
                delete current.entries[keyDate];
        }
        await this._putKvdb(key, current);
    }

    _buildLeaderboardEntries() {
        const metric = this._leaderboardMetric;
        const period = this._leaderboardPeriod;
        const today = dayKey();
        this._leaderboardEntries = (this._leaderboardRecords ?? []).map(({id, data}) => {
            let value = null;
            if (metric === 'collections') {
                const dates = Object.keys(data.entries ?? {}).filter(key => key <= today).sort().reverse();
                for (const date of dates) {
                    if (data.entries[date]?.collections?.length) {
                        value = Number(data.entries[date].collections[0].value); break;
                    }
                }
            } else if (metric === 'luck') {
                value = Number(data.entries?.[today]?.luck?.[0]?.value ?? NaN);
            } else if (period === 0 && data.schema_version >= 2 && metric in (data.totals ?? {})) {
                value = Number(data.totals[metric]);
            } else {
                const days = period || 100000;
                value = 0;
                let found = false;
                for (let offset = 0; offset < days; offset++) {
                    const date = new Date(); date.setDate(date.getDate() - offset);
                    const item = data.entries?.[dayKey(date)]?.[metric]?.[0];
                    if (item) { value += Number(item.value ?? 0); found = true; }
                    if (period === 0 && offset > 3650) break;
                }
                if (!found) value = null;
            }
            return {uuid: id, name: data.name || id, value};
        }).filter(entry => Number.isFinite(entry.value))
            .sort((a, b) => b.value - a.value);
    }

    _formatLeaderboardValue(value) {
        return this._leaderboardMetric === 'active' ? this._formatLeaderboardTime(value) : this._number(value);
    }

    async _getKvdb(key) {
        const {status, text} = await this._sendHttp('GET', `${LEADERBOARD_URL}/${key}`);
        if (status === 404)
            return null;
        if (status < 200 || status >= 300)
            throw new Error(`HTTP ${status}`);
        let value = text;
        for (let layer = 0; layer < 4 && typeof value === 'string'; layer++)
            value = JSON.parse(value);
        return value;
    }

    async _putKvdb(key, value) {
        const body = JSON.stringify(JSON.stringify(value));
        const {status} = await this._sendHttp('PUT', `${LEADERBOARD_URL}/${key}`, body);
        if (status < 200 || status >= 300)
            throw new Error(`HTTP ${status}`);
    }

    _sendHttp(method, uri, body = null) {
        return new Promise((resolve, reject) => {
            try {
                const message = Soup.Message.new(method, uri);
                if (body !== null) {
                    const bytes = new GLib.Bytes(new TextEncoder().encode(body));
                    message.set_request_body_from_bytes('text/plain; charset=utf-8', bytes);
                }
                this._httpSession.send_and_read_async(message, GLib.PRIORITY_DEFAULT, null, (session, result) => {
                    try {
                        const bytes = session.send_and_read_finish(result);
                        resolve({status: message.statusCode, text: new TextDecoder().decode(bytes.get_data())});
                    } catch (error) { reject(error); }
                });
            } catch (error) { reject(error); }
        });
    }

    _renderNavigation() {
        const row = new St.BoxLayout({x_align: Clutter.ActorAlign.CENTER});
        [['数', 'data'], ['统', 'stats'], ['榜', 'leaderboard'], ['性', 'performance'], ['设', 'settings']]
            .forEach(([text, page]) => row.add_child(this._button(text, () => {
                this._page = page; this._render();
                if (page === 'leaderboard') this._refreshLeaderboard();
            }, this._page === page)));
        this._panel.add_child(row);
    }

    _capture(event) {
        if (!this._enabled)
            return Clutter.EVENT_PROPAGATE;
        const type = event.type();
        const now = GLib.get_monotonic_time();
        if (type === Clutter.EventType.BUTTON_PRESS && event.get_button() === 1 &&
            this._store.data.config.locked && this._lockActor) {
            const [x, y] = event.get_coords();
            const [lockX, lockY] = this._lockActor.get_transformed_position();
            const [lockWidth, lockHeight] = this._lockActor.get_transformed_size();
            if (x >= lockX && x < lockX + lockWidth &&
                y >= lockY && y < lockY + lockHeight) {
                this._store.data.config.locked = false;
                this._store.save();
                this._render();
                return Clutter.EVENT_STOP;
            }
        }
        if (type === Clutter.EventType.BUTTON_PRESS && event.get_button() === 1 &&
            !this._store.data.config.locked && !this._dragState && !this._resizeState) {
            const [x, y] = event.get_coords();
            const edge = this._resizeEdgeAt(x, y);
            if (edge)
                return this._beginResize(event, edge);
        }
        if (type === Clutter.EventType.MOTION) {
            this._lastMouse = now; this._lastInput = now; this._bucketInput = true;
        } else if (type === Clutter.EventType.BUTTON_PRESS) {
            const button = event.get_button();
            if (button === 1 || button === 3) {
                const day = this._store.day();
                if (button === 1) day.left++; else day.right++;
                this._inputNow(day, 1, 0); this._lastInput = now; this._bucketInput = true;
            }
        } else if (type === Clutter.EventType.KEY_PRESS) {
            const symbol = event.get_key_symbol();
            if (!this._pressed.has(symbol)) {
                this._pressed.add(symbol);
                const day = this._store.day(); day.keys++; this._classifyKey(day, symbol);
                this._inputNow(day, 0, 1); this._lastInput = now; this._bucketInput = true;
            }
        } else if (type === Clutter.EventType.KEY_RELEASE) {
            this._pressed.delete(event.get_key_symbol());
        }
        return Clutter.EVENT_PROPAGATE;
    }

    _tick() {
        if (!this._enabled)
            return GLib.SOURCE_REMOVE;
        const mono = GLib.get_monotonic_time();
        const elapsed = (mono - this._lastTick) / 1000000;
        const day = this._store.day();
        const uptime = this._uptime();
        const boot = new Date(Date.now() - uptime * 1000);
        if (dayKey(boot) === dayKey()) {
            day.powered = Math.max(day.powered, Math.floor(uptime));
            day.awake = Math.max(day.awake, Math.min(day.powered, Math.floor(mono / 1000000)));
        } else if (elapsed > 0 && elapsed < 5) {
            day.powered += Math.round(elapsed); day.awake += Math.round(elapsed);
        }
        day.longestUptime = Math.max(day.longestUptime, Math.floor(uptime));
        const locked = Main.sessionMode.isLocked;
        if (!locked && elapsed > 0 && elapsed < 5) day.app += Math.round(elapsed);
        const bucket = Math.floor(Date.now() / 5000);
        if (bucket !== this._bucket) {
            if (this._bucketInput) day.active += 5;
            this._bucket = bucket; this._bucketInput = false;
        }
        if (!locked) this._sampleDesktop(day, mono);
        this._lastTick = mono;
        this._maybeCollection();
        const remaining = this._store.data.config.timerEnd - Date.now();
        if (this._store.data.config.timerEnd > 0 && remaining <= 0) {
            this._store.data.config.timerEnd = 0;
            this._timerBubble?.destroy();
            this._timerBubble = this._timerTitle = this._timerValue = null;
            this._showTimerDone();
        } else if (this._timerValue && remaining > 0) {
            this._timerValue.text = this._format(Math.ceil(remaining / 1000));
            this._placeTimerBubble();
        }
        if (this._titleLabel)
            this._titleLabel.label = this._pageTitle();
        if (this._page === 'data' || this._page === 'performance') this._render();
        return GLib.SOURCE_CONTINUE;
    }

    _sampleDesktop(day, mono) {
        if ((mono - this._lastMouse) / 1000000 > 5) day.mouseIdle++;
        const [x, y] = global.get_pointer();
        const monitor = Main.layoutManager.monitors.find(m => x >= m.x && x < m.x + m.width && y >= m.y && y < m.y + m.height);
        if (monitor) {
            const atX = x === monitor.x || x === monitor.x + monitor.width - 1;
            const atY = y === monitor.y || y === monitor.y + monitor.height - 1;
            if (atX || atY) day.edge++;
            if (atX && atY) day.corner++;
            const center = Math.abs(x - (monitor.x + monitor.width / 2)) <= monitor.width * 0.025 &&
                Math.abs(y - (monitor.y + monitor.height / 2)) <= monitor.height * 0.025;
            this._centerStreak = center ? this._centerStreak + 1 : 0;
            if (this._centerStreak > 3) day.center++;
        }
        if ((mono - this._lastInput) / 1000000 <= 5) {
            const wmClass = (global.display.focus_window?.get_wm_class() ?? '').toLowerCase();
            if (['qq', 'linuxqq', 'tim'].some(name => wmClass.includes(name))) day.qq++;
            if (['wechat', 'weixin', 'com.tencent.wechat'].some(name => wmClass.includes(name))) day.wechat++;
        }
    }

    _readProcessMetrics() {
        let status = '';
        let stat = '';
        let meminfo = '';
        try {
            status = new TextDecoder().decode(GLib.file_get_contents('/proc/self/status')[1]);
            stat = new TextDecoder().decode(GLib.file_get_contents('/proc/self/stat')[1]);
            meminfo = new TextDecoder().decode(GLib.file_get_contents('/proc/meminfo')[1]);
        } catch (_) {}
        const readKb = (text, name) => Number(text.match(new RegExp(`^${name}:\\s+(\\d+)`, 'm'))?.[1] ?? 0);
        const rssKb = readKb(status, 'VmRSS');
        const totalKb = readKb(meminfo, 'MemTotal');
        const threads = Number(status.match(/^Threads:\s+(\d+)/m)?.[1] ?? 0);
        let handles = 0;
        try {
            const enumerator = Gio.File.new_for_path('/proc/self/fd').enumerate_children(
                'standard::name', Gio.FileQueryInfoFlags.NONE, null);
            while (enumerator.next_file(null)) handles++;
            enumerator.close(null);
        } catch (_) {}
        let cpu = 0;
        const fields = stat.slice(stat.lastIndexOf(')') + 2).trim().split(/\s+/);
        const ticks = Number(fields[11] ?? 0) + Number(fields[12] ?? 0);
        const now = GLib.get_monotonic_time();
        if (this._processSample) {
            const elapsed = (now - this._processSample.time) / 1000000;
            if (elapsed > 0)
                cpu = Math.max(0, Math.min(100,
                    (ticks - this._processSample.ticks) / 100 / elapsed /
                    Math.max(1, GLib.get_num_processors()) * 100));
        }
        this._processSample = {ticks, time: now};
        const baseHz = this._readBaseCpuHz();
        return {
            cpuPercent: cpu,
            cpuHz: cpu / 100 * baseHz * Math.max(1, GLib.get_num_processors()),
            threads,
            handles,
            memoryMb: rssKb / 1024,
            memoryPercent: totalKb ? rssKb / totalKb * 100 : 0,
        };
    }

    _readBaseCpuHz() {
        try {
            const [ok, bytes] = GLib.file_get_contents('/sys/devices/system/cpu/cpu0/cpufreq/base_frequency');
            if (ok) return Number.parseFloat(new TextDecoder().decode(bytes)) * 1000;
        } catch (_) {}
        try {
            const [ok, bytes] = GLib.file_get_contents('/proc/cpuinfo');
            if (ok) {
                const mhz = new TextDecoder().decode(bytes).match(/^cpu MHz\s*:\s*([\d.]+)/m)?.[1];
                if (mhz) return Number.parseFloat(mhz) * 1000000;
            }
        } catch (_) {}
        return 3000000000;
    }

    _inputNow(day, clicks, keys) {
        const second = Math.floor(Date.now() / 1000);
        if (second !== this._rateSecond) {
            this._rateSecond = second; this._secondClicks = 0; this._secondKeys = 0;
        }
        this._secondClicks += clicks;
        this._secondKeys += keys;
        day.maxCps = Math.max(day.maxCps, this._secondClicks);
        day.maxKps = Math.max(day.maxKps, this._secondKeys);
        day.maxAps = Math.max(day.maxAps, this._secondClicks + this._secondKeys);
    }

    _classifyKey(day, key) {
        if ([Clutter.KEY_w, Clutter.KEY_a, Clutter.KEY_s, Clutter.KEY_d].includes(key)) day.wasd++;
        if ([Clutter.KEY_q, Clutter.KEY_w, Clutter.KEY_e, Clutter.KEY_r].includes(key)) day.qwer++;
        if ([Clutter.KEY_Shift_L, Clutter.KEY_Shift_R].includes(key)) day.shift++;
        if ([Clutter.KEY_Control_L, Clutter.KEY_Control_R].includes(key)) day.ctrl++;
        if (key === Clutter.KEY_Tab) day.tab++;
        if (key === Clutter.KEY_space) day.space++;
        if (key === Clutter.KEY_BackSpace) day.backspace++;
        if ([Clutter.KEY_Return, Clutter.KEY_KP_Enter].includes(key)) day.enter++;
        if ([Clutter.KEY_Up, Clutter.KEY_Down, Clutter.KEY_Left, Clutter.KEY_Right].includes(key)) day.arrows++;
    }

    _readPowerHistory() {
        try {
            const flags = Gio.SubprocessFlags.STDOUT_PIPE | Gio.SubprocessFlags.STDERR_PIPE;
            const launcher = new Gio.SubprocessLauncher({flags});
            launcher.setenv('LC_ALL', 'C', true);
            const process = launcher.spawnv(['last', '-x', '-F', '--time-format', 'iso']);
            process.communicate_utf8_async(null, null, (source, result) => {
                try {
                    const [, stdout] = source.communicate_utf8_finish(result);
                    this._history = this._parseHistory(stdout ?? '');
                    if (this._enabled) this._render();
                } catch (_) {}
            });
        } catch (_) {}
    }

    _parseHistory(text) {
        const result = [];
        const regex = /\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:[.,]\d+)?(?:Z|[+-]\d{2}:?\d{2})/g;
        for (const line of text.split('\n')) {
            if (!line.trim().startsWith('reboot ')) continue;
            const dates = line.match(regex) ?? [];
            if (dates.length) result.push([Date.parse(dates[0]), dates.length > 1 ? Date.parse(dates[1]) : Date.now()]);
        }
        return result.filter(([start, end]) => Number.isFinite(start) && Number.isFinite(end) && end > start);
    }

    _poweredToday(uptime) {
        const start = new Date(); start.setHours(0, 0, 0, 0);
        const end = new Date(start); end.setDate(end.getDate() + 1);
        const intervals = [...this._history, [Date.now() - uptime * 1000, Date.now()]];
        return Math.floor(intervals.reduce((sum, [a, b]) => sum + Math.max(0, Math.min(b, end.getTime()) - Math.max(a, start.getTime())), 0) / 1000);
    }

    _recent(count) {
        const result = [];
        for (let offset = count - 1; offset >= 0; offset--) {
            const date = new Date(); date.setDate(date.getDate() - offset);
            result.push({...emptyDay(), ...(this._store.data.days[dayKey(date)] ?? {})});
        }
        return result;
    }

    _showAllStatistics() {
        const all = Object.values(this._store.data.days).map(day => ({...emptyDay(), ...day}));
        const sum = key => all.reduce((total, day) => total + day[key], 0);
        const peak = key => all.reduce((value, day) => Math.max(value, day[key]), 0);
        const avg = key => all.length ? all.reduce((total, day) => total + day[key], 0) / all.length : 0;
        this._showText('全部统计', [
            `累计使用本应用的时间：${this._format(sum('app'))}`,
            `累计高强度使用时间：${this._format(sum('active'))}`,
            `高强度使用时间峰值：${this._format(peak('active'))}`,
            `累计鼠标点击：${this._number(sum('left') + sum('right'))}`,
            `累计左键点击：${this._number(sum('left'))}`,
            `累计右键点击：${this._number(sum('right'))}`,
            `累计键盘输入：${this._number(sum('keys'))}`,
            `累计wasd输入：${this._number(sum('wasd'))}`,
            `累计qwer输入：${this._number(sum('qwer'))}`,
            `累计↑↓←→输入：${this._number(sum('arrows'))}`,
            `累计shift输入：${this._number(sum('shift'))}`,
            `累计ctrl输入：${this._number(sum('ctrl'))}`,
            `累计tab输入：${this._number(sum('tab'))}`,
            `累计空格输入：${this._number(sum('space'))}`,
            `累计backspace输入：${this._number(sum('backspace'))}`,
            `累计enter输入：${this._number(sum('enter'))}`,
            `平均峰值cps：${avg('maxCps').toFixed(2)}`,
            `平均峰值kps：${avg('maxKps').toFixed(2)}`,
            `平均峰值aps：${avg('maxAps').toFixed(2)}`,
            `QQ使用时间：${this._format(sum('qq'))}`,
            `微信使用时间：${this._format(sum('wechat'))}`,
            `鼠标静止时间：${this._format(sum('mouseIdle'))}`,
            `鼠标边缘停留时间：${this._format(sum('edge'))}`,
            `鼠标角落停留时间：${this._format(sum('corner'))}`,
            `鼠标保持中心时间：${this._format(sum('center'))}`,
            `电脑最长未关机时长：${this._format(peak('longestUptime'))}`,
        ].join('\n'));
    }

    _showText(title, text) {
        const dialog = new ModalDialog.ModalDialog();
        dialog.contentLayout.add_child(this._label(title, 'yunxi-title'));
        const label = new St.Label({text, x_expand: true, style_class: 'yunxi-dialog-text',
            x_align: Clutter.ActorAlign.START, y_align: Clutter.ActorAlign.START});
        label.clutter_text.set_line_wrap(true);
        label.clutter_text.set_ellipsize(Pango.EllipsizeMode.NONE);
        const body = new St.BoxLayout({vertical: true, x_expand: true});
        body.add_child(label);
        const scroll = new St.ScrollView({style_class: 'yunxi-dialog-scroll', overlay_scrollbars: true});
        scroll.set_child(body);
        dialog.contentLayout.add_child(scroll);
        dialog.setButtons([{label: '关闭', action: () => dialog.close(), key: Clutter.KEY_Escape}]);
        GLib.idle_add(GLib.PRIORITY_DEFAULT_IDLE, () => {
            try {
                dialog.open();
            } catch (error) {
                logError(error, `打开${title}失败`);
                dialog.destroy();
                this._showNotice(title, '窗口打开失败，请查看系统日志');
            }
            return GLib.SOURCE_REMOVE;
        });
    }

    _compareVersions(left, right) {
        const a = String(left).replace(/^v/i, '').split('.').map(value => Number(value) || 0);
        const b = String(right).replace(/^v/i, '').split('.').map(value => Number(value) || 0);
        for (let index = 0; index < Math.max(a.length, b.length); index++) {
            if ((a[index] ?? 0) !== (b[index] ?? 0))
                return (a[index] ?? 0) - (b[index] ?? 0);
        }
        return 0;
    }

    _setSettingsStatus(text) {
        this._settingsStatus = text;
        if (this._page === 'settings')
            this._render();
    }

    async _checkForUpdates(interactive = true) {
        if (this._updateChecking || this._updateInstalling)
            return;
        this._updateChecking = true;
        this._setSettingsStatus('正在检测更新...');
        try {
            const release = await this._getLatestRelease();
            const version = String(release.tag_name ?? '').replace(/^v/i, '');
            if (!version)
                throw new Error('更新版本信息无效');
            if (this._compareVersions(version, APP_VERSION) <= 0) {
                this._setSettingsStatus('当前已是最新版本');
                if (interactive)
                    this._showNotice('云曦PC统计更新', '当前已是最新版本');
                return;
            }
            const assets = release.assets ?? [];
            const packageAsset = assets.find(asset => asset.name === UPDATE_ASSET_NAME);
            if (!packageAsset?.browser_download_url)
                throw new Error('Linux 更新包尚未发布');
            const digest = String(packageAsset.digest ?? '').toLowerCase();
            if (!/^sha256:[a-f0-9]{64}$/.test(digest))
                throw new Error('Linux 更新包缺少校验信息');
            const update = {version, packageUrl: packageAsset.browser_download_url,
                checksum: digest.slice('sha256:'.length)};
            this._setSettingsStatus(`发现新版本 ${version}`);
            if (interactive)
                this._showUpdateDialog(update);
            else
                Main.notify('云曦PC统计', `发现新版本 ${version}`);
        } catch (error) {
            this._setSettingsStatus('检测更新失败');
            if (interactive)
                this._showNotice('云曦PC统计更新', error.message ?? '检测更新失败');
        } finally {
            this._updateChecking = false;
        }
    }

    _showUpdateDialog(update) {
        const dialog = new ModalDialog.ModalDialog();
        dialog.contentLayout.add_child(this._label('云曦PC统计更新', 'yunxi-title'));
        dialog.contentLayout.add_child(this._label(`发现新版本 ${update.version}`, 'yunxi-notice-message'));
        dialog.setButtons([
            {label: '下载更新', action: () => { dialog.close(); this._installUpdate(update); }},
            {label: '取消', action: () => dialog.close(), key: Clutter.KEY_Escape},
        ]);
        dialog.open();
    }

    async _getLatestRelease() {
        const text = await this._downloadText(RELEASE_API_URL);
        return JSON.parse(text);
    }

    async _installUpdate(update) {
        if (this._updateInstalling)
            return;
        this._updateInstalling = true;
        this._setSettingsStatus('正在下载更新...');
        try {
            const bytes = await this._downloadFromMirrors(update.packageUrl);
            const actual = GLib.compute_checksum_for_bytes(GLib.ChecksumType.SHA256, bytes).toLowerCase();
            if (actual !== update.checksum)
                throw new Error('更新包校验失败');
            const cacheDir = GLib.build_filenamev([GLib.get_user_cache_dir(), 'yunxi', 'updates', update.version]);
            GLib.mkdir_with_parents(cacheDir, 0o700);
            const zipPath = GLib.build_filenamev([cacheDir, UPDATE_ASSET_NAME]);
            const temporaryPath = `${zipPath}.download`;
            GLib.file_set_contents(temporaryPath, bytes.get_data());
            GLib.rename(temporaryPath, zipPath);
            this._setSettingsStatus('正在安装更新...');
            this._launchUpdateHelper(cacheDir, zipPath, update.version);
            this._showNotice('云曦PC统计更新', '下载完成，正在安装更新');
        } catch (error) {
            this._setSettingsStatus('更新失败');
            this._showNotice('云曦PC统计更新', error.message ?? '更新失败');
        } finally {
            this._updateInstalling = false;
        }
    }

    async _downloadText(url) {
        const bytes = await this._downloadBytes(url);
        return new TextDecoder().decode(bytes.get_data());
    }

    async _downloadFromMirrors(url) {
        let lastError;
        for (const source of [...UPDATE_MIRRORS.map(mirror => `${mirror}${url}`), url]) {
            try {
                return await this._downloadBytes(source);
            } catch (error) {
                lastError = error;
            }
        }
        throw lastError ?? new Error('所有更新源均不可用');
    }

    _downloadBytes(url) {
        return new Promise((resolve, reject) => {
            try {
                if (!String(url).startsWith('https://'))
                    throw new Error('更新地址无效');
                const message = Soup.Message.new('GET', url);
                message.request_headers.append('User-Agent', 'YunXiStatistician');
                this._httpSession.send_and_read_async(message, GLib.PRIORITY_DEFAULT, null, (session, result) => {
                    try {
                        const bytes = session.send_and_read_finish(result);
                        if (message.statusCode < 200 || message.statusCode >= 300)
                            throw new Error(`HTTP ${message.statusCode}`);
                        resolve(bytes);
                    } catch (error) { reject(error); }
                });
            } catch (error) { reject(error); }
        });
    }

    _watchUpdateResult() {
        const resultPath = GLib.build_filenamev([GLib.get_user_cache_dir(), 'yunxi', 'update-result']);
        let attempts = 0;
        this._updateResultId = GLib.timeout_add_seconds(GLib.PRIORITY_DEFAULT, 1, () => {
            attempts++;
            try {
                const [ok, bytes] = GLib.file_get_contents(resultPath);
                if (ok) {
                    const [status, version = ''] = new TextDecoder().decode(bytes).trim().split('|', 2);
                    GLib.unlink(resultPath);
                    const suffix = /^[0-9.]+$/.test(version) ? ` ${version}` : '';
                    if (status === 'success')
                        Main.notify('云曦PC统计', `已更新到版本${suffix}`);
                    else if (status === 'rollback')
                        Main.notify('云曦PC统计', '更新失败，已恢复旧版本');
                    else
                        Main.notify('云曦PC统计', '更新失败，旧版本恢复失败');
                    this._updateResultId = 0;
                    return GLib.SOURCE_REMOVE;
                }
            } catch (_) {
            }
            if (attempts >= 15 || !this._enabled) {
                this._updateResultId = 0;
                return GLib.SOURCE_REMOVE;
            }
            return GLib.SOURCE_CONTINUE;
        });
    }

    _launchUpdateHelper(cacheDir, zipPath, version) {
        const helperPath = GLib.build_filenamev([cacheDir, 'install-update.sh']);
        const backupPath = GLib.build_filenamev([cacheDir, 'backup']);
        const extensionPath = GLib.build_filenamev([GLib.get_user_data_dir(), 'gnome-shell', 'extensions', UUID]);
        const resultPath = GLib.build_filenamev([GLib.get_user_cache_dir(), 'yunxi', 'update-result']);
        try { GLib.unlink(resultPath); } catch (_) {}
        const script = `#!/usr/bin/env bash
set -u
export LC_ALL=C
zip_path="$1"
uuid="$2"
extension_path="$3"
backup_path="$4"
result_path="$5"
version="$6"
write_result() {
  result_tmp="${result_path}.tmp"
  printf '%s|%s\n' "$1" "$version" > "$result_tmp"
  mv -f "$result_tmp" "$result_path"
}
restore_backup() {
  [[ -d "$backup_path" ]] || return 1
  gnome-extensions disable "$uuid" || true
  rm -rf "$extension_path" || return 1
  cp -a "$backup_path" "$extension_path" || return 1
  gnome-extensions enable "$uuid" || return 1
  sleep 2
  gnome-extensions info "$uuid" | grep -q 'State: ENABLED'
}
rm -rf "$backup_path"
if [[ -d "$extension_path" ]]; then
  cp -a "$extension_path" "$backup_path"
fi
if ! gnome-extensions install --force "$zip_path"; then
  if restore_backup; then write_result rollback; else write_result failed; fi
  exit 1
fi
gnome-extensions disable "$uuid" || true
gnome-extensions enable "$uuid" || true
sleep 2
if gnome-extensions info "$uuid" | grep -q 'State: ENABLED'; then
  write_result success
  rm -rf "$backup_path"
  exit 0
fi
if restore_backup; then write_result rollback; else write_result failed; fi
exit 1
`;
        GLib.file_set_contents(helperPath, script);
        GLib.chmod(helperPath, 0o700);
        Gio.Subprocess.new(['bash', helperPath, zipPath, UUID, extensionPath, backupPath,
            resultPath, version],
            Gio.SubprocessFlags.NONE);
    }

    _showNotice(title, message) {
        const dialog = new ModalDialog.ModalDialog();
        const content = new St.BoxLayout({vertical: true, style_class: 'yunxi-notice'});
        content.add_child(this._label(title, 'yunxi-title'));
        content.add_child(this._label(message, 'yunxi-notice-message'));
        dialog.contentLayout.add_child(content);
        dialog.setButtons([{label: '确定', action: () => dialog.close(), key: Clutter.KEY_Escape}]);
        dialog.open();
    }

    _readChangelog() {
        try {
            const [ok, bytes] = this.dir.get_child('changelog.txt').load_contents(null);
            if (ok)
                return new TextDecoder().decode(bytes);
        } catch (_) {
        }
        return '更新日志读取失败';
    }

    _paintChart(area, points, statsView, chartKind, period) {
        const cr = area.get_context();
        const [width, height] = area.get_surface_size();
        const time = statsView === 0;
        const names = time
            ? ['综合折线图', '运行时间', '非睡眠时间', '高强度使用']
            : ['鼠标点击总数', '左键点击', '右键点击', '键盘敲击'];
        const select = time
            ? [point => Math.max(point.powered, point.awake, point.active), point => point.powered,
                point => point.awake, point => point.active]
            : [point => point.mouseTotal, point => point.mouseLeft, point => point.mouseRight, point => point.keyboard];
        const colors = [[0.10, 0.36, 0.65], [0.18, 0.62, 0.42], [0.85, 0.33, 0.31], [0.56, 0.27, 0.68]];
        const selected = Math.max(0, Math.min(3, chartKind));
        const series = time && selected === 0 ? [1, 2, 3] : [selected];
        const rawMax = Math.max(1, ...points.flatMap(point => series.map(index => select[index](point))));
        const max = time ? Math.max(1, Math.ceil(rawMax / 3600)) : Math.max(1, Math.ceil(rawMax));
        const plot = {left: 46, top: 30, right: Math.max(48, width - 10), bottom: Math.max(34, height - 28)};
        const plotWidth = Math.max(1, plot.right - plot.left);
        const plotHeight = Math.max(1, plot.bottom - plot.top);
        const text = (value, x, y, size = 9, align = 'left') => {
            cr.selectFontFace('Sans', 0, 0); cr.setFontSize(size);
            const extent = cr.textExtents(value);
            const left = align === 'center' ? x - extent.width / 2 : align === 'right' ? x - extent.width : x;
            cr.moveTo(left, y); cr.showText(value);
        };
        cr.setSourceRGBA(0.07, 0.10, 0.14, 1);
        text(`过去${period}天 ${names[selected]}`, width / 2, 17, 11, 'center');
        cr.setLineWidth(1);
        for (let index = 0; index <= 4; index++) {
            const ratio = index / 4;
            const y = plot.bottom - ratio * plotHeight;
            cr.setDash([2, 2], 0); cr.setSourceRGBA(0.65, 0.68, 0.72, 0.65);
            cr.moveTo(plot.left, y); cr.lineTo(plot.right, y); cr.stroke();
            cr.setDash([], 0); cr.setSourceRGBA(0.18, 0.21, 0.25, 1);
            const axisValue = time ? this._formatChartTime(max * ratio * 3600) : this._formatChartCount(max * ratio);
            text(axisValue, plot.left - 5, y + 3, 8, 'right');
        }
        const interval = Math.max(1, Math.ceil(points.length / 6));
        points.forEach((point, index) => {
            if (index !== 0 && index !== points.length - 1 && index % interval !== 0) return;
            const x = plot.left + index * plotWidth / Math.max(1, points.length - 1);
            cr.setDash([2, 2], 0); cr.setSourceRGBA(0.65, 0.68, 0.72, 0.45);
            cr.moveTo(x, plot.top); cr.lineTo(x, plot.bottom); cr.stroke(); cr.setDash([], 0);
            cr.setSourceRGBA(0.18, 0.21, 0.25, 1);
            text(`${String(point.date.getMonth() + 1).padStart(2, '0')}-${String(point.date.getDate()).padStart(2, '0')}`,
                x, plot.bottom + 15, 8, 'center');
        });
        cr.setSourceRGBA(0.32, 0.36, 0.40, 1);
        cr.rectangle(plot.left, plot.top, plotWidth, plotHeight); cr.stroke();
        for (const index of series) {
            cr.setSourceRGBA(...colors[index], 1); cr.setLineWidth(1.7);
            points.forEach((point, pointIndex) => {
                const value = select[index](point);
                const normalized = time ? value / 3600 / max : value / max;
                const x = plot.left + pointIndex * plotWidth / Math.max(1, points.length - 1);
                const y = plot.bottom - normalized * plotHeight;
                if (pointIndex === 0) cr.moveTo(x, y); else cr.lineTo(x, y);
            });
            cr.stroke();
        }
        cr.$dispose();
    }

    _metric(name, value, parent = this._content) {
        const row = new St.BoxLayout({vertical: true, x_expand: true, height: 46});
        const nameLabel = this._label(name, 'yunxi-name');
        const valueLabel = this._label(value, value.includes('\n') ? 'yunxi-perf-value' : 'yunxi-value');
        nameLabel.x_align = Clutter.ActorAlign.START;
        valueLabel.x_align = Clutter.ActorAlign.START;
        row.add_child(nameLabel);
        row.add_child(valueLabel);
        this._makeDragSource(nameLabel);
        this._makeDragSource(valueLabel);
        parent.add_child(row);
    }

    _button(text, action, selected = false) {
        const button = new St.Button({style_class: 'yunxi-button', can_focus: true});
        button.set_child(this._label(text, 'yunxi-button-label'));
        if (selected) button.add_style_pseudo_class('selected');
        button.connect('clicked', action);
        return button;
    }

    _label(text, style) {
        const label = new St.Label({text, style_class: style, y_align: Clutter.ActorAlign.CENTER});
        label.clutter_text.set_ellipsize(Pango.EllipsizeMode.NONE);
        return label;
    }
    _dragLabel(text, style) { return this._makeDragSource(this._label(text, style)); }
    _pageTitle() { const base = {data: ['当日', '输入统计', '当日极值'][this._store.data.config.dataView], stats: '统计', leaderboard: '排行榜', performance: '组件性能', settings: '设置'}[this._page]; const left = Math.max(0, this._store.data.config.timerEnd - Date.now()); return left ? `${base} · ${this._format(Math.ceil(left / 1000))}` : base; }
    _uptime() { try { const [, bytes] = GLib.file_get_contents('/proc/uptime'); return Number.parseFloat(new TextDecoder().decode(bytes).split(' ')[0]) || 0; } catch (_) { return 0; } }
    _format(seconds) { seconds = Math.max(0, Math.floor(seconds)); const days = Math.floor(seconds / 86400); const h = String(Math.floor(seconds % 86400 / 3600)).padStart(2, '0'); const m = String(Math.floor(seconds % 3600 / 60)).padStart(2, '0'); const s = String(seconds % 60).padStart(2, '0'); return days ? `${days}天 ${h}:${m}` : `${h}:${m}:${s}`; }
    _formatLeaderboardTime(seconds) {
        seconds = Math.max(0, Math.floor(seconds));
        const hours = String(Math.floor(seconds / 3600)).padStart(2, '0');
        const minutes = String(Math.floor(seconds % 3600 / 60)).padStart(2, '0');
        const remainder = String(seconds % 60).padStart(2, '0');
        return `${hours}:${minutes}:${remainder}`;
    }
    _formatChartTime(seconds) {
        const minutes = Math.max(0, Math.round(seconds / 60));
        return `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}`;
    }
    _formatChartCount(value) {
        if (value >= 100000000) return `${(value / 100000000).toFixed(1)}亿`;
        if (value >= 10000) return `${(value / 10000).toFixed(1)}万`;
        return Math.round(value).toLocaleString();
    }
    _formatFrequency(hz) {
        if (hz >= 1000000000) return `${(hz / 1000000000).toFixed(2).replace(/0+$/, '').replace(/\.$/, '')} GHz`;
        if (hz >= 1000000) return `${(hz / 1000000).toFixed(2).replace(/0+$/, '').replace(/\.$/, '')} MHz`;
        if (hz >= 1000) return `${(hz / 1000).toFixed(2).replace(/0+$/, '').replace(/\.$/, '')} KHz`;
        return `${Math.round(hz)} Hz`;
    }
    _formatMemory(mb) {
        if (mb >= 1024) return `${(mb / 1024).toFixed(2).replace(/0+$/, '').replace(/\.$/, '')} GB`;
        return `${mb.toFixed(1).replace(/\.0$/, '')} MB`;
    }
    _number(value) { return Math.floor(value).toLocaleString(); }
    _setVisible(visible) {
        this._store.data.config.hidden = !visible;
        if (this._timerBubble) this._timerBubble.visible = visible;
        this._store.save(); this._render();
    }
    _toggleVisible() { this._setVisible(this._store.data.config.hidden); }
    _startTimer(minutes) { this._startTimerSeconds(minutes * 60); }
    _startTimerSeconds(seconds) {
        this._store.data.config.timerEnd = seconds > 0 ? Date.now() + seconds * 1000 : 0;
        if (seconds <= 0) {
            this._timerBubble?.destroy();
            this._timerBubble = this._timerTitle = this._timerValue = null;
        } else {
            if (!this._timerBubble) {
                this._timerBubble = new St.BoxLayout({style_class: 'yunxi-timer-bubble'});
                this._timerTitle = this._label('计时器', 'yunxi-timer-title');
                this._timerValue = this._label(this._format(seconds), 'yunxi-timer-value');
                const close = this._button('关', () => this._startTimerSeconds(0));
                this._timerBubble.add_child(this._timerTitle);
                this._timerBubble.add_child(this._timerValue);
                this._timerBubble.add_child(new St.Widget({x_expand: true}));
                this._timerBubble.add_child(close);
                Main.layoutManager.addChrome(this._timerBubble, {trackFullscreen: true});
            }
            this._timerValue.text = this._format(seconds);
            this._timerBubble.visible = !this._store.data.config.hidden;
            this._placeTimerBubble();
        }
        this._store.save(); this._render();
    }
    _placeTimerBubble() {
        if (!this._timerBubble || !this._root)
            return;
        const monitor = Main.layoutManager.monitors.find(item =>
            this._root.x >= item.x && this._root.x < item.x + item.width) ?? Main.layoutManager.primaryMonitor;
        if (!monitor)
            return;
        const x = Math.max(monitor.x, Math.min(this._root.x, monitor.x + monitor.width - 180));
        const above = this._root.y - 42;
        const y = above >= monitor.y
            ? above : this._root.y + this._root.height * this._store.data.config.scale;
        this._timerBubble.set_position(x, y);
    }
    _paintCollectionArt(area, kind, colors) {
        const cr = area.get_context();
        const [primary, secondary, accent] = colors;
        const setColor = hex => {
            const value = Number.parseInt(hex.slice(1), 16);
            cr.setSourceRGBA((value >> 16) / 255, ((value >> 8) & 255) / 255, (value & 255) / 255, 1);
        };
        const polygon = (points, fill, stroke = secondary) => {
            cr.moveTo(points[0][0], points[0][1]);
            for (const [x, y] of points.slice(1)) cr.lineTo(x, y);
            cr.closePath(); setColor(fill); cr.fillPreserve(); setColor(stroke); cr.stroke();
        };
        const line = (x1, y1, x2, y2, color = secondary) => {
            cr.moveTo(x1, y1); cr.lineTo(x2, y2); setColor(color); cr.stroke();
        };
        const ellipse = (x, y, width, height, fill, stroke = secondary) => {
            cr.save(); cr.translate(x + width / 2, y + height / 2); cr.scale(width / 2, height / 2);
            cr.arc(0, 0, 1, 0, Math.PI * 2); cr.restore();
            setColor(fill); cr.fillPreserve(); setColor(stroke); cr.stroke();
        };
        cr.setLineWidth(1);
        if (kind === 'diamond') {
            polygon([[10, 0.5], [19, 10], [10, 19.5], [1, 10]], primary);
            polygon([[10, 0.5], [14, 10], [10, 19.5], [6, 10]], accent);
            line(10, 0.5, 10, 19.5); line(1, 10, 19, 10); line(6, 5, 14, 5); line(6, 15, 14, 15);
        } else if (kind === 'candy') {
            polygon([[2, 8], [7, 5], [7, 15], [2, 12]], primary, primary);
            polygon([[18, 8], [13, 5], [13, 15], [18, 12]], primary, primary);
            ellipse(5, 4, 10, 12, secondary, accent);
            line(8, 4, 8, 16, accent); line(12, 4, 12, 16, accent);
        } else if (kind === 'crystal') {
            polygon([[10, 0.5], [16, 9], [11, 19.5], [5, 13]], primary);
            polygon([[10, 0.5], [16, 9], [8, 12], [4, 7]], accent);
            polygon([[5, 13], [8, 12], [11, 19.5]], secondary);
            line(10, 0.5, 8, 12); line(4, 7, 16, 9);
        } else if (kind === 'pumpkin') {
            ellipse(1, 7, 18, 12, primary); ellipse(4, 7, 12, 12, primary);
            line(5, 8, 5, 19); line(10, 8, 10, 19); line(15, 8, 15, 19);
            cr.rectangle(9, 3, 2, 5); setColor(accent); cr.fillPreserve(); setColor(secondary); cr.stroke();
            polygon([[6, 11], [9, 11], [7, 14]], secondary, secondary);
            polygon([[11, 11], [14, 11], [13, 14]], secondary, secondary); line(7, 16, 13, 16);
        } else if (kind === 'emerald') {
            polygon([[4, 2], [16, 2], [18, 7], [18, 13], [16, 18], [4, 18], [2, 13], [2, 7]], primary);
            polygon([[4, 2], [16, 2], [10, 10], [4, 18]], accent, accent);
            polygon([[2, 7], [4, 2], [4, 18], [2, 13]], secondary, secondary);
            polygon([[18, 7], [16, 2], [16, 18], [18, 13]], secondary, secondary);
            polygon([[2, 7], [10, 10], [18, 7]], accent, accent);
            polygon([[2, 13], [10, 10], [18, 13]], accent, accent);
            line(4, 2, 16, 2); line(4, 18, 16, 18);
        } else if (kind === 'round') {
            ellipse(2, 2, 16, 16, primary);
            polygon([[10, 2], [16, 6], [16, 14], [10, 18], [4, 14], [4, 6]], accent);
            line(10, 2, 10, 18); line(4, 6, 16, 6); line(4, 14, 16, 14);
            line(7, 4, 10, 10); line(13, 4, 10, 10);
        } else if (kind === 'teardrop') {
            polygon([[10, 1], [17, 7], [17, 16], [10, 19], [3, 16], [3, 7]], primary);
            polygon([[10, 1], [17, 7], [10, 12], [3, 7]], accent);
            polygon([[3, 7], [10, 12], [10, 19], [3, 16]], secondary, secondary);
            polygon([[17, 7], [10, 12], [10, 19], [17, 16]], secondary, secondary);
            line(10, 1, 10, 19); line(3, 7, 17, 7);
        } else {
            polygon([[10, 18], [2, 9], [5, 3], [9, 4]], primary);
            polygon([[10, 18], [18, 9], [15, 3], [11, 4]], primary);
            polygon([[10, 18], [2, 9], [7, 7], [10, 11]], accent, accent);
            polygon([[10, 18], [18, 9], [13, 7], [10, 11]], accent, accent);
            line(10, 4, 10, 18); line(2, 9, 18, 9);
        }
        cr.$dispose();
    }
    _maybeCollection() {
        const minute = Math.floor(Date.now() / 60000);
        if (minute === this._collectionMinute || this._collectionVisible)
            return;
        this._collectionMinute = minute;
        if (Math.random() >= 0.02 || !this._root?.visible)
            return;
        this._collectionVisible = true;
        const [kind, ...colors] = COLLECTION_ARTS[Math.floor(Math.random() * COLLECTION_ARTS.length)];
        const art = new St.DrawingArea({width: 20, height: 20});
        art.connect('repaint', area => this._paintCollectionArt(area, kind, colors));
        const ball = new St.Button({style_class: 'yunxi-collectible', reactive: true});
        ball.set_child(art);
        ball.set_size(20, 20);
        const [rootWidth, rootHeight] = this._root.get_transformed_size();
        const width = Math.max(1, rootWidth - 40);
        const height = Math.max(1, rootHeight - 64);
        ball.set_position(this._root.x + 10 + Math.floor(Math.random() * width),
            this._root.y + 32 + Math.floor(Math.random() * height));
        ball.connect('clicked', () => {
            this._store.data.config.collections++;
            this._store.save();
            this._collectionVisible = false;
            this._collectionActor = null;
            ball.destroy();
            if (this._page === 'leaderboard') this._render();
        });
        this._collectionActor = ball;
        Main.layoutManager.addChrome(ball, {trackFullscreen: true});
    }
    _disableSelf() { Gio.DBus.session.call('org.gnome.Shell.Extensions', '/org/gnome/Shell/Extensions', 'org.gnome.Shell.Extensions', 'DisableExtension', new GLib.Variant('(s)', [UUID]), null, Gio.DBusCallFlags.NONE, -1, null, null); }
}
