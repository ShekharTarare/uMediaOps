import { LitElement, html, css } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'
import './umediaops-logo.js'

export class uMediaOpsSidebarMenu extends UmbElementMixin(LitElement) {
  static styles = css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      background: var(--uui-color-surface);
    }

    .logo-container {
      padding: var(--uui-size-space-5);
      border-bottom: 1px solid var(--uui-color-border);
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .menu-items {
      flex: 1;
      overflow-y: auto;
    }

    .menu-item {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-3) var(--uui-size-space-4);
      cursor: pointer;
      text-decoration: none;
      color: var(--uui-color-text);
      transition: background 200ms ease;
      border-left: 3px solid transparent;
    }

    .menu-item:hover {
      background: var(--uui-color-surface-emphasis);
    }

    .menu-item.active {
      background: var(--uui-color-surface-emphasis);
      border-left-color: var(--umediaops-primary-color, #00B5A3);
      font-weight: 600;
    }

    .menu-item uui-icon {
      font-size: 20px;
      color: var(--umediaops-primary-color, #00B5A3);
    }
  `

  render() {
    return html`
      <div class="logo-container">
        <umediaops-logo size="large" .showText=${true}></umediaops-logo>
      </div>
      <div class="menu-items">
        <a href="#/uMediaOps/overview" class="menu-item">
          <uui-icon name="icon-home"></uui-icon>
          <span>Overview</span>
        </a>
        <a href="#/uMediaOps/duplicate-detection" class="menu-item">
          <uui-icon name="icon-search"></uui-icon>
          <span>Duplicate Detection</span>
        </a>
        <a href="#/uMediaOps/duplicates" class="menu-item">
          <uui-icon name="icon-files"></uui-icon>
          <span>Duplicate Groups</span>
        </a>
        <a href="#/uMediaOps/unused-media" class="menu-item">
          <uui-icon name="icon-trash"></uui-icon>
          <span>Unused Media</span>
        </a>
        <a href="#/uMediaOps/backup" class="menu-item">
          <uui-icon name="icon-save"></uui-icon>
          <span>Backup</span>
        </a>
        <a href="#/uMediaOps/audit-log" class="menu-item">
          <uui-icon name="icon-list"></uui-icon>
          <span>Audit Log</span>
        </a>
      </div>
    `
  }
}

customElements.define('umediaops-sidebar-menu', uMediaOpsSidebarMenu)
