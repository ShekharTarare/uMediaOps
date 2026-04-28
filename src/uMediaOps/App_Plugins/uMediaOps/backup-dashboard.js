import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'
import { NotificationHelper } from './notification-helper.js'
import { AuthenticationHelper } from './authentication-helper.js'

export class BackupDashboard extends UmbElementMixin(LitElement) {
  static properties = {
    backups: { type: Array },
    isCreating: { type: Boolean },
    progress: { type: Object },
    isLoading: { type: Boolean },
    error: { type: String },
    showConfirmDialog: { type: Boolean },
    confirmDialogData: { type: Object },
  }

  constructor() {
    super()
    this.backups = []
    this.isCreating = false
    this.progress = { processedFiles: 0, totalFiles: 0, percentage: 0 }
    this.isLoading = false
    this.error = null
    this.showConfirmDialog = false
    this.confirmDialogData = null
    this.pollInterval = null
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
      await this.loadBackups()
    } catch (error) {
      this.handleAuthError(error)
    }
  }

  disconnectedCallback() {
    super.disconnectedCallback()
    this.stopPolling()
    if (this.authHelper) {
      this.authHelper.destroy()
    }
  }

  async loadBackups() {
    try {
      this.isLoading = true
      this.error = null

      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/backup/list',
      )

      if (response.status === 404) {
        this.backups = []
        this.isLoading = false
        return
      }

      if (!response.ok) {
        throw new Error('Failed to load backups')
      }

      const data = await response.json()

      // Normalize property names
      this.backups = data.map((b) => ({
        backupId: b.backupId || b.BackupId,
        fileName: b.fileName || b.FileName,
        backupType: b.backupType || b.BackupType,
        storageProvider: b.storageProvider || b.StorageProvider,
        createdAt: b.createdAt || b.CreatedAt,
        createdBy: b.createdBy || b.CreatedBy,
        fileCount: b.fileCount || b.FileCount || 0,
        totalSize: b.totalSize || b.TotalSize || 0,
        isVerified: b.isVerified || b.IsVerified || false,
      }))
    } catch (err) {
      // Silently handle errors - don't log to console
      this.error = `Failed to load backups: ${err.message}`
      NotificationHelper.showError(this, this.error)
    } finally {
      this.isLoading = false
    }
  }

  startPolling() {
    this.pollInterval = setInterval(async () => {
      await this.checkProgress()
    }, 2000)
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
        '/umbraco/management/api/v1/umediaops/backup/create/progress',
      )
      if (!response.ok) return

      const data = await response.json()
      this.progress = data.progress || data

      if (!data.isBackingUp) {
        this.stopPolling()
        this.isCreating = false
        await this.loadBackups()

        NotificationHelper.showSuccess(this, 'Backup created successfully')
      }
    } catch (err) {
      // Silently handle errors - don't log to console
      this.stopPolling()
      this.isCreating = false
    }
  }

  async createBackup() {
    try {
      this.error = null
      this.isCreating = true

      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/backup/create',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            backupType: 'Full',
            storageProvider: 'Local',
          }),
        },
      )

      if (!response.ok) {
        this.isCreating = false
        if (response.status === 429) {
          NotificationHelper.showWarning(
            this,
            'Please wait before starting another backup.',
          )
          return
        }
        if (response.status === 400) {
          const data = await response.json().catch(() => ({}))
          NotificationHelper.showWarning(
            this,
            data.message || 'A backup is already in progress.',
          )
          return
        }
        throw new Error('Failed to create backup')
      }

      this.startPolling()
      NotificationHelper.showSuccess(this, 'Backup export started')
    } catch (err) {
      this.isCreating = false
      this.error = err.message
      NotificationHelper.showError(
        this,
        `Failed to create backup: ${err.message}`,
      )
    }
  }

  openConfirmDialog(type, data) {
    this.confirmDialogData = { type, ...data }
    this.showConfirmDialog = true
  }

  closeConfirmDialog() {
    this.showConfirmDialog = false
    this.confirmDialogData = null
  }

  async verifyBackup(backupId) {
    try {
      const response = await this.makeAuthenticatedRequest(
        `/umbraco/management/api/v1/umediaops/backup/${backupId}/verify`,
        { method: 'POST' },
      )

      if (!response.ok) throw new Error('Failed to verify backup')

      const result = await response.json()

      if (result.isValid) {
        NotificationHelper.showSuccess(
          this,
          'Backup integrity verified successfully',
        )
      } else {
        NotificationHelper.showWarning(this, 'Backup integrity check failed')
      }

      await this.loadBackups()
    } catch (err) {
      NotificationHelper.showError(
        this,
        `Failed to verify backup: ${err.message}`,
      )
    }
  }

  async downloadBackup(backupId, fileName) {
    try {
      const response = await this.makeAuthenticatedRequest(
        `/umbraco/management/api/v1/umediaops/backup/${backupId}/download`,
      )

      if (!response.ok) throw new Error('Failed to download backup')

      const blob = await response.blob()
      const blobUrl = URL.createObjectURL(blob)

      // Use an anchor tag to trigger the download
      const anchor = document.createElement('a')
      anchor.href = blobUrl
      anchor.download = fileName
      anchor.click()

      // Cleanup after a delay
      setTimeout(() => URL.revokeObjectURL(blobUrl), 1000)

      NotificationHelper.showSuccess(this, 'Backup downloaded successfully')
    } catch (err) {
      NotificationHelper.showError(
        this,
        `Failed to download backup: ${err.message}`,
      )
    }
  }

  async confirmDelete(backupId) {
    try {
      const response = await this.makeAuthenticatedRequest(
        `/umbraco/management/api/v1/umediaops/backup/${backupId}`,
        { method: 'DELETE' },
      )

      if (!response.ok) throw new Error('Failed to delete backup')

      this.closeConfirmDialog()
      await this.loadBackups()
      NotificationHelper.showSuccess(this, 'Backup deleted successfully')
    } catch (err) {
      NotificationHelper.showError(
        this,
        `Failed to delete backup: ${err.message}`,
      )
      this.closeConfirmDialog()
    }
  }

  formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes'
    const k = 1024
    const sizes = ['Bytes', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i]
  }

  formatDate(dateString) {
    return new Date(dateString).toLocaleString()
  }

  formatTimeAgo(dateString) {
    if (!dateString) return 'Never'

    // Ensure UTC dates from the backend are parsed correctly
    let utcDate = dateString
    if (
      !utcDate.endsWith('Z') &&
      !utcDate.includes('+') &&
      !utcDate.includes('-', 10)
    ) {
      utcDate += 'Z'
    }

    const date = new Date(utcDate)
    if (isNaN(date.getTime())) return 'Unknown'

    const now = new Date()
    const seconds = Math.floor((now - date) / 1000)

    if (seconds < 0) return 'just now'
    if (seconds < 60) return 'just now'
    const minutes = Math.floor(seconds / 60)
    if (minutes < 60) return `${minutes} minute${minutes > 1 ? 's' : ''} ago`
    const hours = Math.floor(minutes / 60)
    if (hours < 24) return `${hours} hour${hours > 1 ? 's' : ''} ago`
    const days = Math.floor(hours / 24)
    return `${days} day${days > 1 ? 's' : ''} ago`
  }

  getConfirmDialogContent() {
    if (!this.confirmDialogData) return null

    const { type, backup } = this.confirmDialogData

    if (type === 'delete') {
      return {
        title: 'Confirm Delete',
        message: `Are you sure you want to delete backup "${backup.fileName}"? This action cannot be undone.`,
        warning: 'The backup file will be permanently deleted.',
        confirmLabel: 'Delete Backup',
        confirmColor: 'danger',
        onConfirm: () => this.confirmDelete(backup.backupId),
      }
    }

    return null
  }

  render() {
    if (this.isLoading) {
      return html`
        <div class="dashboard-container">
          <div class="header">
            <h1>
              <uui-icon name="icon-box"></uui-icon>
              Backup Management
            </h1>
          </div>
          <div class="loading">
            <uui-loader></uui-loader>
            <p>Loading backup data...</p>
          </div>
        </div>
      `
    }

    return html`
      <div class="dashboard-container">
        <div class="header">
          <div class="title-section">
            <h1>
              <uui-icon name="icon-box"></uui-icon>
              Backup Management
            </h1>
            <span class="package-badge">uMediaOps</span>
          </div>
          <p class="subtitle">
            Export backups of your media library files for safekeeping
          </p>
        </div>

        ${this.error
          ? html`
              <uui-box>
                <p style="color: var(--uui-color-danger);">${this.error}</p>
              </uui-box>
            `
          : ''}

        <uui-box class="create-box">
          <div class="create-content">
            <h2>
              <uui-icon name="icon-add"></uui-icon>
              Export Media Backup
            </h2>
            <p class="description">
              Export a complete backup of your media library files. This creates
              a downloadable ZIP archive for safekeeping.
            </p>

            <div class="create-form">
              <uui-button
                look="primary"
                color="positive"
                label="Export Backup"
                @click=${this.createBackup}
                ?disabled=${this.isCreating}
                class="create-button"
              >
                <uui-icon name="icon-add"></uui-icon>
                ${this.isCreating ? 'Exporting Backup...' : 'Export Backup'}
              </uui-button>
            </div>

            ${this.isCreating
              ? html`
                  <div class="progress-section" aria-busy="true">
                    <div
                      role="status"
                      aria-live="polite"
                      aria-atomic="true"
                      class="sr-only"
                    >
                      Export in progress: ${this.progress.percentage}% complete.
                    </div>
                    <div class="progress-stats">
                      <span class="progress-text"
                        >Processing: ${this.progress.processedFiles} /
                        ${this.progress.totalFiles}</span
                      >
                      <span class="progress-percentage"
                        >${this.progress.percentage}%</span
                      >
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
          </div>
        </uui-box>

        <uui-box class="backups-box">
          <div class="backups-content">
            <h2>
              <uui-icon name="icon-folder"></uui-icon>
              Exported Backups
            </h2>

            ${this.backups.length > 0
              ? html`
                  <div class="backups-list">
                    ${this.backups.map(
                      (backup) => html`
                        <div class="backup-card">
                          <div class="backup-header">
                            <div class="backup-info">
                              <h3>${backup.fileName}</h3>
                              <div class="backup-meta">
                                <span>
                                  <uui-icon name="icon-calendar"></uui-icon>
                                  ${this.formatTimeAgo(backup.createdAt)}
                                </span>
                                <span class="separator">•</span>
                                <span>
                                  <uui-icon name="icon-user"></uui-icon>
                                  ${backup.createdBy}
                                </span>
                                <span class="separator">•</span>
                                <span>
                                  <uui-icon name="icon-cloud"></uui-icon>
                                  ${backup.storageProvider}
                                </span>
                              </div>
                            </div>
                            ${backup.isVerified
                              ? html`
                                  <span class="verified-badge">
                                    <uui-icon name="icon-check"></uui-icon>
                                    Verified
                                  </span>
                                `
                              : ''}
                          </div>

                          <div class="backup-stats">
                            <div class="stat-item">
                              <uui-icon name="icon-files"></uui-icon>
                              <span
                                ><strong>${backup.fileCount}</strong>
                                files</span
                              >
                            </div>
                            <div class="stat-item">
                              <uui-icon name="icon-box"></uui-icon>
                              <span
                                ><strong
                                  >${this.formatBytes(backup.totalSize)}</strong
                                >
                                total size</span
                              >
                            </div>
                          </div>

                          <div class="backup-actions">
                            <uui-button
                              look="outline"
                              label="Download"
                              compact
                              @click=${() =>
                                this.downloadBackup(
                                  backup.backupId,
                                  backup.fileName,
                                )}
                              ?disabled=${this.isCreating}
                            >
                              <uui-icon name="icon-download-alt"></uui-icon>
                              Download
                            </uui-button>
                            <uui-button
                              look="outline"
                              label="Verify"
                              compact
                              @click=${() => this.verifyBackup(backup.backupId)}
                              ?disabled=${this.isCreating}
                            >
                              <uui-icon name="icon-check"></uui-icon>
                              Verify
                            </uui-button>
                            <uui-button
                              look="outline"
                              color="danger"
                              label="Delete"
                              compact
                              @click=${() =>
                                this.openConfirmDialog('delete', { backup })}
                              ?disabled=${this.isCreating}
                            >
                              <uui-icon name="icon-trash"></uui-icon>
                              Delete
                            </uui-button>
                          </div>
                        </div>
                      `,
                    )}
                  </div>
                `
              : html`
                  <div class="empty-state">
                    <uui-icon name="icon-box"></uui-icon>
                    <p>No backups available. Export your first backup above.</p>
                  </div>
                `}
          </div>
        </uui-box>

        ${this.showConfirmDialog && this.confirmDialogData
          ? html`
              <div class="dialog-overlay" @click=${this.closeConfirmDialog}>
                <div class="dialog" @click=${(e) => e.stopPropagation()}>
                  ${(() => {
                    const content = this.getConfirmDialogContent()
                    return html`
                      <h2>${content.title}</h2>
                      <p>${content.message}</p>
                      ${content.warning
                        ? html`<p
                            style="color: var(--uui-color-warning); font-weight: 500;"
                          >
                            ⚠️ ${content.warning}
                          </p>`
                        : ''}
                      <div class="dialog-actions">
                        <uui-button
                          look="outline"
                          label="Cancel"
                          @click=${this.closeConfirmDialog}
                          >Cancel</uui-button
                        >
                        <uui-button
                          look="primary"
                          color="${content.confirmColor}"
                          label="${content.confirmLabel}"
                          @click=${content.onConfirm}
                        >
                          ${content.confirmLabel}
                        </uui-button>
                      </div>
                    `
                  })()}
                </div>
              </div>
            `
          : ''}
      </div>
    `
  }

  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      padding: 0;
      margin: -1px;
      overflow: hidden;
      clip: rect(0, 0, 0, 0);
      white-space: nowrap;
      border-width: 0;
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

    .loading {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 40px;
      gap: 16px;
    }

    .create-box,
    .restore-progress-box,
    .backups-box {
      margin-bottom: 16px;
    }

    .create-content,
    .restore-content,
    .backups-content {
      padding: 24px;
    }

    h2 {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      margin-top: 0;
      margin-bottom: var(--uui-size-space-3);
    }

    .description {
      color: var(--uui-color-text-alt);
      margin-bottom: var(--uui-size-space-4);
      line-height: 1.6;
    }

    .create-form {
      display: flex;
      gap: 16px;
      align-items: flex-end;
      flex-wrap: wrap;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
      min-width: 200px;
    }

    .form-group label {
      font-weight: 500;
      color: var(--uui-color-text);
    }

    .form-group select {
      padding: 10px 12px;
      border: 2px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface);
      color: var(--uui-color-text);
      font-size: 0.95rem;
      font-family: inherit;
    }

    .create-button {
      min-width: 180px;
    }

    .progress-section {
      margin-top: 16px;
      padding: 16px;
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      border: 1px solid var(--uui-color-border);
    }

    .progress-stats {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: var(--uui-size-space-3);
    }

    .progress-text {
      font-weight: 500;
    }

    .progress-percentage {
      font-size: 1.25rem;
      font-weight: bold;
      color: var(--uui-color-positive);
    }

    .progress-bar {
      width: 100%;
      height: 32px;
      background: var(--uui-color-surface);
      border-radius: var(--uui-border-radius);
      overflow: hidden;
      border: 1px solid var(--uui-color-border);
    }

    .progress-fill {
      height: 100%;
      background: linear-gradient(90deg, #00b5a3 0%, #1e293b 100%);
      transition: width 0.3s ease;
    }

    .backups-list {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .backup-card {
      padding: 20px;
      border: 2px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface-alt);
      transition:
        transform 0.2s,
        box-shadow 0.2s;
    }

    .backup-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
    }

    .backup-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 16px;
    }

    .backup-info h3 {
      margin: 0 0 8px 0;
      font-size: 1.1rem;
    }

    .backup-meta {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 0.9rem;
      color: var(--uui-color-text-alt);
      flex-wrap: wrap;
    }

    .backup-meta span {
      display: flex;
      align-items: center;
      gap: 4px;
    }

    .separator {
      color: var(--uui-color-border);
    }

    .verified-badge {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      background: var(--uui-color-positive);
      color: white;
      border-radius: 6px;
      font-weight: 600;
      font-size: 0.85rem;
    }

    .backup-stats {
      display: flex;
      gap: 24px;
      margin-bottom: 16px;
      padding: 12px;
      background: var(--uui-color-surface);
      border-radius: 6px;
    }

    .stat-item {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 0.9rem;
    }

    .stat-item uui-icon {
      color: var(--uui-color-text-alt);
    }

    .backup-actions {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 60px 20px;
      text-align: center;
    }

    .empty-state uui-icon {
      font-size: 4rem;
      color: var(--uui-color-text-alt);
      margin-bottom: 16px;
    }

    .empty-state p {
      color: var(--uui-color-text-alt);
    }

    .dialog-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }

    .dialog {
      background: var(--uui-color-surface);
      padding: 24px;
      border-radius: var(--uui-border-radius);
      max-width: 500px;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.2);
    }

    .dialog h2 {
      margin-top: 0;
    }

    .dialog-actions {
      display: flex;
      gap: 12px;
      justify-content: flex-end;
      margin-top: 16px;
    }
  `
}

customElements.define('umediaops-backup-dashboard', BackupDashboard)
