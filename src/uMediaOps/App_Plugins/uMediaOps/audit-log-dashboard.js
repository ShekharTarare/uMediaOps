import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'
import { AuthenticationHelper } from './authentication-helper.js'
import { NotificationHelper } from './notification-helper.js'

export class AuditLogDashboard extends UmbElementMixin(LitElement) {
  static properties = {
    entries: { type: Array },
    loading: { type: Boolean },
    error: { type: String },
    filterAction: { type: String },
    filterUser: { type: String },
    expandedIds: { type: Set },
    currentPage: { type: Number },
    itemsPerPage: { type: Number },
  }

  constructor() {
    super()
    this.entries = []
    this.loading = false
    this.error = null
    this.filterAction = 'all'
    this.filterUser = 'all'
    this.expandedIds = new Set()
    this.currentPage = 1
    this.itemsPerPage = 10
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
      this.loadEntries()
    } catch (error) {
      this.handleAuthError(error)
    }
  }

  async loadEntries() {
    this.loading = true
    this.error = null

    try {
      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/auditlog/recent',
      )

      if (!response.ok) {
        throw new Error('Failed to load audit log entries')
      }

      this.entries = await response.json()
    } catch (err) {
      this.error = err.message
    } finally {
      this.loading = false
    }
  }

  toggleExpanded(id) {
    if (this.expandedIds.has(id)) {
      this.expandedIds.delete(id)
    } else {
      this.expandedIds.add(id)
    }
    this.expandedIds = new Set(this.expandedIds)
  }

  getFilteredEntries() {
    return this.entries.filter((entry) => {
      let actionMatch = this.filterAction === 'all'
      if (!actionMatch) {
        const action = entry.action.toLowerCase()
        const filter = this.filterAction.toLowerCase()

        if (filter === 'delete') {
          actionMatch = action.includes('delete') && !action.includes('bulk')
        } else if (filter === 'scan') {
          actionMatch = action.includes('scan')
        } else if (filter === 'bulkdelete') {
          actionMatch = action.includes('bulkdelete') || action.includes('bulk')
        } else if (filter === 'backup') {
          actionMatch = action.includes('backup')
        } else {
          actionMatch = action.includes(filter)
        }
      }

      const userMatch =
        this.filterUser === 'all' || entry.userName === this.filterUser
      return actionMatch && userMatch
    })
  }

  getUniqueUsers() {
    const users = new Set(this.entries.map((e) => e.userName))
    return Array.from(users).sort()
  }

  getActionIcon(action) {
    const a = action.toLowerCase()
    if (a.includes('delete') && a.includes('bulk')) return 'icon-trash'
    if (a.includes('delete') && a.includes('blocked')) return 'icon-block'
    if (a.includes('delete') && a.includes('error')) return 'icon-alert'
    if (a.includes('delete') && a.includes('hard')) return 'icon-delete'
    if (a.includes('delete')) return 'icon-trash'
    if (a.includes('scan') && a.includes('fail')) return 'icon-alert'
    if (a.includes('scan')) return 'icon-search'
    if (a.includes('backup') && a.includes('verified')) return 'icon-check'
    if (a.includes('backup') && a.includes('deleted')) return 'icon-trash'
    if (a.includes('backup') && a.includes('fail')) return 'icon-alert'
    if (a.includes('backup')) return 'icon-save'
    return 'icon-info'
  }

  getActionColor(action) {
    const a = action.toLowerCase()
    if (a.includes('error') || a.includes('fail')) return 'danger'
    if (a.includes('blocked')) return 'warning'
    if (a.includes('delete') && a.includes('hard')) return 'danger'
    if (a.includes('delete')) return 'warning'
    if (a.includes('scan') && a.includes('completed')) return 'positive'
    if (a.includes('backup') && a.includes('created')) return 'positive'
    if (a.includes('backup') && a.includes('verified')) return 'positive'
    return 'default'
  }

  formatDate(dateString) {
    return new Date(dateString).toLocaleString()
  }

  formatDetails(details) {
    if (!details) return 'No additional details'
    try {
      const parsed = JSON.parse(details)
      return Object.entries(parsed)
        .map(([key, value]) => {
          // Format byte values
          if (
            key.toLowerCase().includes('size') ||
            key.toLowerCase().includes('wasted')
          ) {
            return `${key}: ${this.formatBytes(value)}`
          }
          return `${key}: ${value}`
        })
        .join(', ')
    } catch {
      return details
    }
  }

  formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes'
    const k = 1024
    const sizes = ['Bytes', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i]
  }

  getPaginatedEntries() {
    const filtered = this.getFilteredEntries()
    const start = (this.currentPage - 1) * this.itemsPerPage
    const end = start + this.itemsPerPage
    return filtered.slice(start, end)
  }

  getTotalPages() {
    const filtered = this.getFilteredEntries()
    return Math.ceil(filtered.length / this.itemsPerPage)
  }

  goToPage(page) {
    this.currentPage = page
  }

  nextPage() {
    if (this.currentPage < this.getTotalPages()) {
      this.currentPage++
    }
  }

  previousPage() {
    if (this.currentPage > 1) {
      this.currentPage--
    }
  }

  render() {
    const filteredEntries = this.getFilteredEntries()
    const uniqueUsers = this.getUniqueUsers()

    return html`
      <div class="dashboard-container">
        <div class="header">
          <div class="title-section">
            <h1>
              <uui-icon name="icon-list"></uui-icon>
              Audit Log
            </h1>
            <span class="package-badge">uMediaOps</span>
          </div>
          <p class="subtitle">
            Track all media operations and changes. View who did what and when.
          </p>
        </div>

        <div class="controls">
          <uui-button look="outline" label="Refresh" @click=${this.loadEntries}>
            <uui-icon name="icon-refresh"></uui-icon>
            Refresh
          </uui-button>

          <div class="filter-group">
            <label>Filter by Action:</label>
            <select
              @change=${(e) => (this.filterAction = e.target.value)}
              .value=${this.filterAction}
            >
              <option value="all">All Actions</option>
              <option value="delete">Delete</option>
              <option value="scan">Scan</option>
              <option value="bulkdelete">Bulk Delete</option>
              <option value="backup">Backup</option>
            </select>
          </div>

          <div class="filter-group">
            <label>Filter by User:</label>
            <select
              @change=${(e) => (this.filterUser = e.target.value)}
              .value=${this.filterUser}
            >
              <option value="all">All Users</option>
              ${uniqueUsers.map(
                (user) => html`<option value="${user}">${user}</option>`,
              )}
            </select>
          </div>
        </div>

        ${this.error
          ? html`
              <uui-box>
                <p style="color: var(--uui-color-danger);">${this.error}</p>
              </uui-box>
            `
          : ''}
        ${this.loading
          ? html`
              <div class="loading">
                <uui-loader></uui-loader>
                <p>Loading audit log...</p>
              </div>
            `
          : ''}
        ${!this.loading && filteredEntries.length === 0
          ? html`
              <uui-box>
                <div class="empty-state">
                  <uui-icon
                    name="icon-info"
                    style="font-size: 3rem; color: var(--uui-color-text-alt);"
                  ></uui-icon>
                  <h2>No Audit Entries</h2>
                  <p>
                    ${this.filterAction !== 'all' || this.filterUser !== 'all'
                      ? 'No entries match the current filters.'
                      : 'No audit log entries found.'}
                  </p>
                </div>
              </uui-box>
            `
          : ''}
        ${!this.loading && filteredEntries.length > 0
          ? html`
              <div class="summary">
                <strong>${filteredEntries.length}</strong>
                entr${filteredEntries.length > 1 ? 'ies' : 'y'}
                ${this.filterAction !== 'all' || this.filterUser !== 'all'
                  ? '(filtered)'
                  : ''}
                ${this.getTotalPages() > 1
                  ? html` - Page ${this.currentPage} of ${this.getTotalPages()}`
                  : ''}
              </div>

              <div class="entries-list">
                ${this.getPaginatedEntries().map(
                  (entry) => html`
                    <uui-box class="entry-card">
                      <div class="entry-header">
                        <div
                          class="action-badge ${this.getActionColor(
                            entry.action,
                          )}"
                        >
                          <uui-icon
                            name="${this.getActionIcon(entry.action)}"
                          ></uui-icon>
                          ${entry.action}
                        </div>
                        <div class="entry-info">
                          <div class="entry-meta">
                            <span
                              ><strong>Media ID:</strong> ${entry.mediaId}</span
                            >
                            <span class="separator">•</span>
                            <span><strong>By:</strong> ${entry.userName}</span>
                            <span class="separator">•</span>
                            <span>${this.formatDate(entry.timestamp)}</span>
                          </div>
                        </div>
                        <uui-button
                          look="outline"
                          compact
                          label="Toggle Details"
                          @click=${() => this.toggleExpanded(entry.id)}
                        >
                          <uui-icon
                            name="${this.expandedIds.has(entry.id)
                              ? 'icon-navigation-up'
                              : 'icon-navigation-down'}"
                          ></uui-icon>
                        </uui-button>
                      </div>

                      ${this.expandedIds.has(entry.id)
                        ? html`
                            <div class="entry-details">
                              <div class="details-section">
                                <strong>Details:</strong>
                                <p>${this.formatDetails(entry.details)}</p>
                              </div>
                              ${entry.success !== undefined
                                ? html`
                                    <div class="details-section">
                                      <strong>Status:</strong>
                                      <span
                                        class="status-badge ${entry.success
                                          ? 'success'
                                          : 'failure'}"
                                      >
                                        ${entry.success ? 'Success' : 'Failed'}
                                      </span>
                                    </div>
                                  `
                                : ''}
                            </div>
                          `
                        : ''}
                    </uui-box>
                  `,
                )}
              </div>

              ${this.getTotalPages() > 1
                ? html`
                    <div class="pagination">
                      <uui-button
                        look="outline"
                        label="Previous"
                        @click=${this.previousPage}
                        ?disabled=${this.currentPage === 1}
                      >
                        <uui-icon name="icon-arrow-left"></uui-icon>
                        Previous
                      </uui-button>

                      <div class="page-numbers">
                        ${Array.from(
                          { length: this.getTotalPages() },
                          (_, i) => i + 1,
                        ).map(
                          (page) => html`
                            <uui-button
                              look="${page === this.currentPage
                                ? 'primary'
                                : 'outline'}"
                              label="Page ${page}"
                              compact
                              @click=${() => this.goToPage(page)}
                            >
                              ${page}
                            </uui-button>
                          `,
                        )}
                      </div>

                      <uui-button
                        look="outline"
                        label="Next"
                        @click=${this.nextPage}
                        ?disabled=${this.currentPage === this.getTotalPages()}
                      >
                        Next
                        <uui-icon name="icon-arrow-right"></uui-icon>
                      </uui-button>
                    </div>
                  `
                : ''}
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

  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    .dashboard-container {
      max-width: 1400px;
      margin: 0 auto;
      width: 100%;
    }

    .header {
      margin-bottom: var(--uui-size-space-6);
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
      background: linear-gradient(135deg, #00b5a3 0%, #1e293b 100%);
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

    .controls {
      display: flex;
      gap: 12px; /* 12px spacing between buttons */
      margin-bottom: 16px; /* 16px spacing between major sections */
      flex-wrap: wrap;
      align-items: center;
      padding: 16px; /* Consistent padding */
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      border: 1px solid var(--uui-color-border);
    }

    .filter-group {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
    }

    .filter-group label {
      font-weight: 500;
      font-size: 0.9rem;
    }

    .filter-group select {
      padding: 8px 12px;
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface);
      font-size: 0.9rem;
      cursor: pointer;
    }

    uui-button {
      transition: all 0.2s ease;
    }

    uui-button:not([disabled]):hover {
      transform: translateY(-1px);
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
    }

    uui-button:not([disabled]):active {
      transform: translateY(0);
      box-shadow: 0 1px 4px rgba(0, 0, 0, 0.1);
    }

    .loading {
      text-align: center;
      padding: var(--uui-size-space-6);
      animation: fadeIn 0.3s ease;
    }

    @keyframes fadeIn {
      from {
        opacity: 0;
        transform: translateY(10px);
      }
      to {
        opacity: 1;
        transform: translateY(0);
      }
    }

    .empty-state {
      text-align: center;
      padding: var(--uui-size-space-6);
      animation: fadeIn 0.3s ease;
    }

    .empty-state h2 {
      margin: var(--uui-size-space-4) 0 var(--uui-size-space-2);
    }

    .summary {
      margin-bottom: 16px; /* 16px spacing between major sections */
      padding: 16px; /* Consistent padding */
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
    }

    .entries-list {
      display: flex;
      flex-direction: column;
      gap: 16px; /* Consistent spacing */
    }

    .entry-card {
      transition:
        transform 0.2s,
        box-shadow 0.2s,
        border-color 0.2s;
      border: 1px solid var(--uui-color-border);
    }

    .entry-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
      border-color: var(--uui-color-interactive);
    }

    .entry-header {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
    }

    .action-badge {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      padding: 8px 16px;
      border-radius: 12px;
      font-weight: 600;
      font-size: 0.9rem;
      white-space: nowrap;
    }

    .action-badge.danger {
      background: #fee;
      color: #c00;
      border: 1px solid #fcc;
    }

    .action-badge.warning {
      background: #fff3cd;
      color: #856404;
      border: 1px solid #ffeaa7;
    }

    .action-badge.positive {
      background: #d4edda;
      color: #155724;
      border: 1px solid #c3e6cb;
    }

    .action-badge.default {
      background: var(--uui-color-surface-alt);
      color: var(--uui-color-text);
      border: 1px solid var(--uui-color-border);
    }

    .entry-info {
      flex: 1;
    }

    .entry-meta {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      font-size: 0.9rem;
      color: var(--uui-color-text-alt);
    }

    .separator {
      color: var(--uui-color-border);
    }

    .entry-details {
      margin-top: var(--uui-size-space-3);
      padding-top: var(--uui-size-space-3);
      border-top: 1px solid var(--uui-color-border);
    }

    .details-section {
      margin-bottom: var(--uui-size-space-2);
    }

    .details-section strong {
      display: block;
      margin-bottom: var(--uui-size-space-1);
    }

    .details-section p {
      margin: 0;
      color: var(--uui-color-text-alt);
    }

    .status-badge {
      padding: 4px 12px;
      border-radius: 8px;
      font-size: 0.85rem;
      font-weight: 600;
    }

    .status-badge.success {
      background: #d4edda;
      color: #155724;
      border: 1px solid #c3e6cb;
    }

    .status-badge.failure {
      background: #fee;
      color: #c00;
      border: 1px solid #fcc;
    }

    .pagination {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-top: 16px; /* 16px spacing between major sections */
      padding: 16px; /* Consistent padding */
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      border: 1px solid var(--uui-color-border);
    }

    .page-numbers {
      display: flex;
      gap: 12px; /* 12px spacing between buttons */
    }
  `
}

customElements.define('umediaops-audit-log-dashboard', AuditLogDashboard)
