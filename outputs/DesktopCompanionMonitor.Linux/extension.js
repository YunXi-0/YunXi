import Gio from 'gi://Gio';

import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

export default class YunXiBootstrap extends Extension {
    async enable() {
        const implementationFile = this.dir.get_child('main.js');
        const info = implementationFile.query_info(
            'time::modified,time::modified-usec', Gio.FileQueryInfoFlags.NONE, null);
        const modified = `${info.get_attribute_uint64('time::modified')}-${info.get_attribute_uint32('time::modified-usec')}`;
        const implementation = await import(`${implementationFile.get_uri()}?version=${modified}`);
        this._implementation = new implementation.default({
            ...this.metadata,
            dir: this.dir,
            path: this.path,
        });
        await this._implementation.enable();
    }

    async disable() {
        await this._implementation?.disable();
        this._implementation = null;
    }
}
