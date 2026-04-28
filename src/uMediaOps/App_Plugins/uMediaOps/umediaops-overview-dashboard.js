import { LitElement, html, css } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'
import { AuthenticationHelper } from './authentication-helper.js'
import { NotificationHelper } from './notification-helper.js'
import './umediaops-logo.js'

export class uMediaOpsOverviewDashboard extends UmbElementMixin(LitElement) {
  static properties = {
    stats: { type: Object },
    loading: { type: Boolean },
    recentActivity: { type: Array },
    savings: { type: Object },
    topFileTypes: { type: Array },
  }

  constructor() {
    super()
    this.stats = null
    this.loading = true
    this.recentActivity = []
    this.savings = null
    this.topFileTypes = []
    this.authHelper = new AuthenticationHelper(this)
  }

  async makeAuthenticatedRequest(url, options = {}) {
    try {
      return await this.authHelper.makeAuthenticatedRequest(url, options)
    } catch (error) {
      this.handleAuthError(error)
      throw error
    }
  }

  handleAuthError(error) {
    NotificationHelper.showError(
      this,
      'Authentication failed. Please ensure you are logged into the Umbraco backoffice.',
    )
  }

  async connectedCallback() {
    super.connectedCallback()

    try {
      await this.authHelper.initialize()
      this.loadDashboardData()
    } catch (error) {
      this.handleAuthError(error)
    }
  }

  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    .hero-banner {
      background: linear-gradient(135deg, #1e293b 0%, #2d3f54 100%);
      border-radius: 10px;
      padding: 16px 24px;
      margin-bottom: var(--uui-size-space-5);
      color: white;
      position: relative;
      overflow: hidden;
    }

    .hero-banner::before {
      content: '';
      position: absolute;
      top: -50%;
      right: -20%;
      width: 300px;
      height: 300px;
      background: radial-gradient(
        circle,
        rgba(255, 255, 255, 0.06) 0%,
        transparent 70%
      );
      pointer-events: none;
    }

    .hero-banner::after {
      content: '';
      position: absolute;
      bottom: -30%;
      left: -10%;
      width: 200px;
      height: 200px;
      background: radial-gradient(
        circle,
        rgba(255, 255, 255, 0.04) 0%,
        transparent 70%
      );
      pointer-events: none;
    }

    .hero-content {
      display: flex;
      align-items: center;
      gap: 14px;
      position: relative;
      z-index: 1;
    }

    .hero-content umediaops-logo {
      flex-shrink: 0;
    }

    .hero-text {
      flex: 1;
    }

    .hero-text h1 {
      margin: 0;
      font-size: 20px;
      font-weight: 800;
      letter-spacing: -0.3px;
    }

    .hero-tagline {
      margin: 1px 0 0;
      font-size: 12px;
      color: rgba(255, 255, 255, 0.75);
      font-weight: 400;
    }

    .hero-version {
      padding: 3px 10px;
      background: rgba(255, 255, 255, 0.15);
      border: 1px solid rgba(255, 255, 255, 0.25);
      border-radius: 12px;
      font-size: 0.7rem;
      font-weight: 600;
      color: white;
      flex-shrink: 0;
    }

    .subtitle {
      color: var(--uui-color-text-alt);
      margin: 0;
      font-size: 1rem;
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: var(--uui-size-space-4);
      margin-bottom: var(--uui-size-space-5);
      animation: fadeIn 0.5s ease-in;
    }

    @media (max-width: 900px) {
      .stats-grid {
        grid-template-columns: repeat(2, 1fr);
      }
    }

    @media (max-width: 500px) {
      .stats-grid {
        grid-template-columns: 1fr;
      }
    }

    @keyframes fadeIn {
      from {
        opacity: 0;
        transform: translateY(20px);
      }
      to {
        opacity: 1;
        transform: translateY(0);
      }
    }

    .stat-card {
      background: var(--uui-color-surface);
      border: 2px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      padding: 20px;
      transition: all 300ms ease;
      animation: slideIn 0.5s ease-out backwards;
      text-align: center;
    }

    .stat-card:nth-child(1) {
      animation-delay: 0.1s;
    }
    .stat-card:nth-child(2) {
      animation-delay: 0.2s;
    }
    .stat-card:nth-child(3) {
      animation-delay: 0.3s;
    }
    .stat-card:nth-child(4) {
      animation-delay: 0.4s;
    }

    @keyframes slideIn {
      from {
        opacity: 0;
        transform: translateX(-20px);
      }
      to {
        opacity: 1;
        transform: translateX(0);
      }
    }

    .stat-card:hover {
      transform: translateY(-4px) scale(1.02);
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
      border-color: var(--umediaops-primary-color, #00b5a3);
    }

    .stat-card.highlight {
      background: linear-gradient(135deg, #00b5a315 0%, #1e293b15 100%);
      border-color: #00b5a3;
    }

    .stat-card.warning {
      background: rgba(245, 158, 11, 0.05);
      border-color: #f59e0b;
    }

    .stat-header {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--uui-size-space-2);
      margin-bottom: var(--uui-size-space-2);
      color: var(--uui-color-text-alt);
      font-size: 0.85rem;
      font-weight: 600;
    }

    .stat-value {
      font-size: 2rem;
      font-weight: bold;
      color: var(--uui-color-text);
      margin-bottom: 4px;
    }

    .stat-card.highlight .stat-value {
      color: #00b5a3;
    }

    .stat-card.warning .stat-value {
      color: #f59e0b;
    }

    .stat-label {
      color: var(--uui-color-text-alt);
      font-size: 0.85rem;
    }

    .quick-actions {
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      padding: var(--uui-size-space-5);
      margin-bottom: var(--uui-size-space-6);
    }

    .quick-actions h2 {
      margin: 0 0 var(--uui-size-space-4) 0;
      font-size: 1.25rem;
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
    }

    .action-message {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-4);
      background: linear-gradient(135deg, #00b5a315 0%, #1e293b15 100%);
      border: 2px solid #00b5a3;
      border-radius: var(--uui-border-radius);
      margin-bottom: var(--uui-size-space-3);
    }

    .action-message uui-icon {
      font-size: 1.5rem;
      color: #00b5a3;
      flex-shrink: 0;
    }

    .action-content {
      flex: 1;
    }

    .action-content strong {
      display: block;
      margin-bottom: 4px;
    }

    .action-content p {
      margin: 0;
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
    }

    .info-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
      gap: var(--uui-size-space-5);
    }

    .info-card {
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      padding: var(--uui-size-space-5);
    }

    .info-card h3 {
      margin: 0 0 var(--uui-size-space-3) 0;
      font-size: 1.1rem;
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
    }

    .info-card.success-card {
      background: linear-gradient(
        135deg,
        rgba(46, 184, 92, 0.05) 0%,
        rgba(31, 166, 122, 0.05) 100%
      );
      border-color: #2eb85c;
    }

    .info-card.success-card h3 {
      color: #2eb85c;
    }

    .info-list {
      list-style: none;
      padding: 0;
      margin: 0;
    }

    .info-list li {
      padding: var(--uui-size-space-2) 0;
      border-bottom: 1px solid var(--uui-color-border);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .info-list li:last-child {
      border-bottom: none;
    }

    .info-label {
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
    }

    .info-value {
      font-weight: 600;
    }

    .info-value.success {
      color: #2eb85c;
      font-size: 1.1rem;
    }

    .info-value.warning {
      color: #f59e0b;
    }

    .empty-state {
      text-align: center;
      padding: var(--uui-size-space-8);
      color: var(--uui-color-text-alt);
    }

    .empty-state uui-icon {
      font-size: 3rem;
      margin-bottom: var(--uui-size-space-3);
      opacity: 0.5;
    }

    .loading {
      text-align: center;
      padding: var(--uui-size-space-6);
    }
  `

  async loadDashboardData() {
    this.stats = {
      totalScanned: 0,
      duplicateGroups: 0,
      duplicateFiles: 0,
      storageWasted: 0,
      lastScanDate: null,
      hasNeverScanned: true,
    }

    try {
      let scanResults = null

      // Always try to load scan results from the API
      try {
        const scanResponse = await this.makeAuthenticatedRequest(
          '/umbraco/management/api/v1/umediaops/scan/results',
        )
        if (scanResponse.ok) {
          scanResults = await scanResponse.json()
          localStorage.setItem('umediaops-has-scan-results', 'true')
        }
      } catch (e) {
        /* ignore */
      }

      // Load analytics data if scan results exist
      if (scanResults) {
        try {
          const savingsResponse = await this.makeAuthenticatedRequest(
            '/umbraco/management/api/v1/umediaops/analytics/savings',
          )
          if (savingsResponse.ok) {
            this.savings = await savingsResponse.json()
          }
        } catch (e) {
          /* ignore */
        }

        try {
          const statsResponse = await this.makeAuthenticatedRequest(
            '/umbraco/management/api/v1/umediaops/analytics/statistics',
          )
          if (statsResponse.ok) {
            const statsData = await statsResponse.json()
            this.topFileTypes = statsData.statistics?.slice(0, 5) || []
          }
        } catch (e) {
          /* ignore */
        }
      }

      this.stats = {
        totalScanned: scanResults?.totalScanned || 0,
        duplicateGroups: scanResults?.duplicateGroupsFound || 0,
        duplicateFiles: scanResults?.totalDuplicates || 0,
        storageWasted: scanResults?.storageWasted || 0,
        lastScanDate: scanResults?.scannedAt || null,
        hasNeverScanned: !scanResults,
      }

      this.loading = false
    } catch (error) {
      this.loading = false
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
    if (minutes < 60) return `${minutes} minute${minutes > 1 ? 's' : ''} ago`
    const hours = Math.floor(minutes / 60)
    if (hours < 24) return `${hours} hour${hours > 1 ? 's' : ''} ago`
    const days = Math.floor(hours / 24)
    return `${days} day${days > 1 ? 's' : ''} ago`
  }

  render() {
    if (this.loading) {
      return html`
        <div class="loading">
          <uui-loader></uui-loader>
          <p>Loading uMediaOps Dashboard...</p>
        </div>
      `
    }

    return html`
      <div class="hero-banner">
        <div class="hero-content">
          <umediaops-logo
            size="medium"
            .showText=${false}
            variant="light"
          ></umediaops-logo>
          <div class="hero-text">
            <h1>
              <span style="color:white">u</span
              ><span style="color:#00B5A3">Media</span
              ><span style="color:white">Ops</span>
            </h1>
            <p class="hero-tagline">Media Management Suite for Umbraco</p>
          </div>
          <span class="hero-version">v1.0.2</span>
        </div>
      </div>

      ${this.stats.hasNeverScanned
        ? html`
            <div class="quick-actions">
              <h2>
                <uui-icon name="icon-lightbulb"></uui-icon>
                Get Started
              </h2>
              <div class="action-message">
                <uui-icon name="icon-info"></uui-icon>
                <div class="action-content">
                  <strong>Welcome to uMediaOps!</strong>
                  <p>
                    Start by running your first scan. Use the "Duplicates" tab
                    above to scan your media library.
                  </p>
                </div>
              </div>
            </div>
          `
        : ''}

      <div class="stats-grid">
        <div class="stat-card">
          <div class="stat-header">
            <uui-icon name="icon-files"></uui-icon>
            <span>Total Files Scanned</span>
          </div>
          <div class="stat-value">${this.stats.totalScanned}</div>
          <div class="stat-label">Media files analyzed</div>
        </div>

        <div class="stat-card">
          <div class="stat-header">
            <uui-icon name="icon-folder"></uui-icon>
            <span>Duplicate Groups</span>
          </div>
          <div class="stat-value">${this.stats.duplicateGroups}</div>
          <div class="stat-label">
            ${this.stats.duplicateGroups > 0
              ? 'Groups found with duplicates'
              : 'No duplicates found'}
          </div>
        </div>

        <div class="stat-card">
          <div class="stat-header">
            <uui-icon name="icon-documents"></uui-icon>
            <span>Duplicate Files</span>
          </div>
          <div class="stat-value">${this.stats.duplicateFiles}</div>
          <div class="stat-label">Files that can be removed</div>
        </div>

        <div
          class="stat-card ${this.stats.storageWasted > 0 ? 'highlight' : ''}"
        >
          <div class="stat-header">
            <uui-icon name="icon-trash"></uui-icon>
            <span>Storage Wasted</span>
          </div>
          <div class="stat-value">
            ${this.formatBytes(this.stats.storageWasted)}
          </div>
          <div class="stat-label">Can be recovered</div>
        </div>
      </div>

      ${this.stats.duplicateGroups > 0
        ? html`
            <div class="quick-actions">
              <h2>
                <uui-icon name="icon-alert"></uui-icon>
                Action Required
              </h2>
              <div class="action-message">
                <uui-icon name="icon-lightbulb"></uui-icon>
                <div class="action-content">
                  <strong>Duplicates detected!</strong>
                  <p>
                    Found ${this.stats.duplicateGroups} groups with
                    ${this.stats.duplicateFiles} duplicate files. Switch to the
                    "Duplicates" tab to review and manage them.
                  </p>
                </div>
              </div>
            </div>
          `
        : ''}

      <div class="info-grid">
        <div class="info-card">
          <h3>
            <uui-icon name="icon-info"></uui-icon>
            System Information
          </h3>
          <ul class="info-list">
            <li>
              <span class="info-label">Last Scan</span>
              <span class="info-value"
                >${this.formatTimeAgo(this.stats.lastScanDate)}</span
              >
            </li>
            <li>
              <span class="info-label">Files Analyzed</span>
              <span class="info-value">${this.stats.totalScanned}</span>
            </li>
            <li>
              <span class="info-label">Duplicate Groups</span>
              <span class="info-value">${this.stats.duplicateGroups}</span>
            </li>
          </ul>
        </div>

        ${this.savings?.totalSaved > 0
          ? html`
              <div class="info-card success-card">
                <h3>
                  <uui-icon name="icon-check"></uui-icon>
                  Your Impact
                </h3>
                <ul class="info-list">
                  <li>
                    <span class="info-label">Space Recovered</span>
                    <span class="info-value success"
                      >${this.formatBytes(this.savings.totalSaved)}</span
                    >
                  </li>
                  <li>
                    <span class="info-label">Files Cleaned</span>
                    <span class="info-value"
                      >${this.savings.dataPoints?.reduce(
                        (sum, p) => sum + p.filesDeleted,
                        0,
                      ) || 0}</span
                    >
                  </li>
                  <li>
                    <span class="info-label">Last Cleanup</span>
                    <span class="info-value"
                      >${this.savings.dataPoints?.length > 0
                        ? this.formatTimeAgo(
                            this.savings.dataPoints[
                              this.savings.dataPoints.length - 1
                            ].date,
                          )
                        : 'Never'}</span
                    >
                  </li>
                </ul>
              </div>
            `
          : html`
              <div class="info-card">
                <h3>
                  <uui-icon name="icon-help"></uui-icon>
                  Quick Tips
                </h3>
                <ul class="info-list">
                  <li>
                    <span class="info-label">Use tabs above to navigate</span>
                  </li>
                  <li>
                    <span class="info-label"
                      >Run scans regularly to keep library clean</span
                    >
                  </li>
                  <li>
                    <span class="info-label"
                      >Review duplicate groups to optimize storage</span
                    >
                  </li>
                </ul>
              </div>
            `}
        ${this.topFileTypes.length > 0
          ? html`
              <div class="info-card">
                <h3>
                  <uui-icon name="icon-files"></uui-icon>
                  Top Duplicate File Types
                </h3>
                <ul class="info-list">
                  ${this.topFileTypes.map(
                    (type) => html`
                      <li>
                        <span class="info-label">.${type.fileType}</span>
                        <span class="info-value"
                          >${type.count} files
                          (${this.formatBytes(type.totalSize)})</span
                        >
                      </li>
                    `,
                  )}
                </ul>
              </div>
            `
          : ''}
      </div>
    `
  }

  disconnectedCallback() {
    super.disconnectedCallback()
    if (this.authHelper) {
      this.authHelper.destroy()
    }
  }
}

customElements.define(
  'umediaops-overview-dashboard',
  uMediaOpsOverviewDashboard,
)
