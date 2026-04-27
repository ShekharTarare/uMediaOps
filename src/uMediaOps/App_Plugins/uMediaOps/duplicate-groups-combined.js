import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'
import './duplicate-groups-list.js'
import './duplicate-group-detail.js'
import { AuthenticationHelper } from './authentication-helper.js'
import { NotificationHelper } from './notification-helper.js'

export class DuplicateGroupsCombined extends UmbElementMixin(LitElement) {
  static properties = {
    currentView: { type: String },
    selectedHash: { type: String },
    isScanning: { type: Boolean },
    progress: { type: Object },
    scanResults: { type: Object },
  }

  constructor() {
    super()
    this.currentView = 'list'
    this.selectedHash = ''
    this.isScanning = false
    this.progress = { processed: 0, total: 0, percentage: 0 }
    this.scanResults = null
    this.pollInterval = null
    this.authHelper = new AuthenticationHelper(this)
  }

  async makeAuthenticatedRequest(url, options = {}) {
    try {
      return await this.authHelper.makeAuthenticatedRequest(url, options)
    } catch (error) {
      NotificationHelper.showError(this, 'Authentication failed.')
      throw error
    }
  }

  async connectedCallback() {
    super.connectedCallback()
    window.addEventListener(
      'popstate',
      (this._handleNav = this.handleNavigation.bind(this)),
    )
    this.handleNavigation()

    try {
      await this.authHelper.initialize()
      await this.loadScanResults()
    } catch (error) {
      // Silently handle
    }
  }

  disconnectedCallback() {
    super.disconnectedCallback()
    window.removeEventListener('popstate', this._handleNav)
    this.stopPolling()
    if (this.authHelper) this.authHelper.destroy()
  }

  handleNavigation() {
    this.currentView = 'list'
    this.selectedHash = ''
  }

  showDetail(hash) {
    this.selectedHash = hash
    this.currentView = 'detail'
  }

  showList() {
    this.currentView = 'list'
    this.selectedHash = ''
    this.requestUpdate()
    this.updateComplete.then(() => {
      const listComponent = this.shadowRoot.querySelector(
        'umediaops-duplicate-groups-list',
      )
      if (listComponent?.loadGroups) listComponent.loadGroups()
    })
  }

  // --- Scan logic ---

  async startScan() {
    try {
      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/scan/start',
        { method: 'POST' },
      )
      if (!response.ok) throw new Error('Failed to start scan')

      this.isScanning = true
      this.scanResults = null
      this.startPolling()
    } catch (err) {
      NotificationHelper.showError(this, `Failed to start scan: ${err.message}`)
    }
  }

  startPolling() {
    this.pollInterval = setInterval(() => this.checkProgress(), 1000)
  }

  stopPolling() {
    if (this.pollInterval) {
      clearInterval(this.pollInterval)
      this.pollInterval = null
    }
  }

  async checkProgress() {
    try {
      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/scan/progress',
      )
      if (!response.ok) return
      const data = await response.json()
      this.isScanning = data.isScanning
      this.progress = data.progress

      if (!data.isScanning) {
        this.stopPolling()
        localStorage.setItem('umediaops-has-scan-results', 'true')
        await this.loadScanResults()

        if (this.scanResults) {
          NotificationHelper.showSuccess(
            this,
            `Scan complete! Found ${this.scanResults.duplicateGroupsFound} groups with ${this.scanResults.totalDuplicates} duplicates.`,
          )
          // Reload the groups list
          this.updateComplete.then(() => {
            const listComponent = this.shadowRoot.querySelector(
              'umediaops-duplicate-groups-list',
            )
            if (listComponent?.loadGroups) listComponent.loadGroups()
          })
        }
      }
    } catch (err) {
      this.stopPolling()
      this.isScanning = false
    }
  }

  async loadScanResults() {
    const hasScanResults =
      localStorage.getItem('umediaops-has-scan-results') === 'true'
    if (!hasScanResults) {
      this.scanResults = null
      return
    }

    try {
      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/scan/results',
      )
      if (response.status === 404) {
        localStorage.setItem('umediaops-has-scan-results', 'false')
        this.scanResults = null
        return
      }
      if (!response.ok) {
        this.scanResults = null
        return
      }
      this.scanResults = await response.json()
    } catch (err) {
      this.scanResults = null
    }
  }

  formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes'
    const k = 1024
    const sizes = ['Bytes', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i]
  }

  formatTimeAgo(dateString) {
    if (!dateString) return 'Never'
    const date = new Date(dateString)
    const now = new Date()
    const seconds = Math.floor((now - date) / 1000)
    if (seconds < 60) return 'just now'
    const minutes = Math.floor(seconds / 60)
    if (minutes < 60) return `${minutes}m ago`
    const hours = Math.floor(minutes / 60)
    if (hours < 24) return `${hours}h ago`
    const days = Math.floor(hours / 24)
    return `${days}d ago`
  }

  render() {
    if (this.currentView === 'detail') {
      return html`
        <umediaops-duplicate-group-detail
          .hash=${this.selectedHash}
          @go-back=${() => this.showList()}
        ></umediaops-duplicate-group-detail>
      `
    }

    return html`
      <div class="page">
        <div class="header">
          <div class="title-section">
            <h1><uui-icon name="icon-files"></uui-icon> Duplicates</h1>
            <span class="package-badge">uMediaOps</span>
          </div>
          <p class="subtitle">
            Scan your media library and manage duplicate files
          </p>
        </div>

        <div class="scan-bar">
          <div class="scan-bar-left">
            <uui-button
              look="primary"
              color="positive"
              label="Scan"
              @click=${this.startScan}
              ?disabled=${this.isScanning}
              compact
            >
              <uui-icon name="icon-refresh"></uui-icon>
              ${this.isScanning ? 'Scanning...' : 'Scan Library'}
            </uui-button>

            ${this.scanResults
              ? html`
                  <span class="scan-stat">
                    <strong>${this.scanResults.totalScanned}</strong> scanned
                  </span>
                `
              : html`
                  <span class="scan-hint">Run a scan to detect duplicates</span>
                `}
          </div>
          <div class="scan-bar-right">
            ${this.scanResults?.scannedAt
              ? html`
                  <span class="scan-time"
                    >Last scan:
                    ${this.formatTimeAgo(this.scanResults.scannedAt)}</span
                  >
                `
              : ''}
          </div>
        </div>

        ${this.isScanning
          ? html`
              <div class="progress-bar-container">
                <div class="progress-info">
                  <span
                    >Processing: ${this.progress.processed} /
                    ${this.progress.total}</span
                  >
                  <span>${this.progress.percentage}%</span>
                </div>
                <div
                  class="progress-bar"
                  role="progressbar"
                  aria-valuenow="${this.progress.percentage}"
                  aria-valuemin="0"
                  aria-valuemax="100"
                >
                  <div
                    class="progress-fill"
                    style="width: ${this.progress.percentage}%"
                  ></div>
                </div>
              </div>
            `
          : ''}

        <umediaops-duplicate-groups-list
          @view-detail=${(e) => this.showDetail(e.detail.hash)}
        ></umediaops-duplicate-groups-list>
      </div>
    `
  }

  static styles = css`
    :host {
      display: block;
    }

    .page {
      padding: var(--uui-size-layout-1);
      max-width: 1400px;
      margin: 0 auto;
    }

    .header {
      margin-bottom: var(--uui-size-space-4);
    }
    .title-section {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      margin-bottom: var(--uui-size-space-2);
    }
    h1 {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      margin: 0;
    }
    .package-badge {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 5px 12px;
      background: linear-gradient(135deg, #00B5A3 0%, #1E293B 100%);
      color: white;
      border-radius: 12px;
      font-size: 0.7rem;
      font-weight: 600;
      letter-spacing: 0.5px;
    }
    .subtitle {
      color: var(--uui-color-text-alt);
      margin: 0;
      font-size: 1rem;
    }

    .scan-bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 12px 16px;
      margin-bottom: 16px;
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      flex-wrap: wrap;
    }
    .scan-bar-left {
      display: flex;
      align-items: center;
      gap: 16px;
      flex-wrap: wrap;
    }
    .scan-bar-right {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .scan-stat {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 4px 10px;
      background: var(--uui-color-surface-alt);
      border-radius: 6px;
      font-size: 0.85rem;
    }
    .scan-stat.warning {
      background: #fff3cd;
      color: #856404;
    }
    .scan-hint {
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
      font-style: italic;
    }
    .scan-time {
      color: var(--uui-color-text-alt);
      font-size: 0.85rem;
    }

    .progress-bar-container {
      margin-bottom: 16px;
      padding: 12px 16px;
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      border: 1px solid var(--uui-color-border);
    }
    .progress-info {
      display: flex;
      justify-content: space-between;
      margin-bottom: 8px;
      font-size: 0.9rem;
    }
    .progress-bar {
      width: 100%;
      height: 8px;
      background: var(--uui-color-surface);
      border-radius: 4px;
      overflow: hidden;
    }
    .progress-fill {
      height: 100%;
      background: linear-gradient(90deg, #00B5A3 0%, #1E293B 100%);
      transition: width 0.3s ease;
    }

    @media (max-width: 768px) {
      .scan-bar {
        flex-direction: column;
        align-items: flex-start;
      }
    }
  `
}

customElements.define(
  'umediaops-duplicate-groups-combined',
  DuplicateGroupsCombined,
)
