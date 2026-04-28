import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'
import './loading-spinner.js'
import { NotificationHelper } from './notification-helper.js'
import { AuthenticationHelper } from './authentication-helper.js'

export class UnusedMediaFinderDashboard extends UmbElementMixin(LitElement) {
  static properties = {
    isScanning: { type: Boolean },
    loading: { type: Boolean },
    progress: { type: Object },
    results: { type: Object },
    selectedItems: { type: Set },
    filters: { type: Object },
    sortBy: { type: String },
    currentPage: { type: Number },
    itemsPerPage: { type: Number },
    totalItems: { type: Number },
    totalPages: { type: Number },
    error: { type: String },
    showConfirmDialog: { type: Boolean },
    confirmDialogData: { type: Object },
    selectedProfile: { type: String },
  }

  constructor() {
    super()
    this.isScanning = false
    this.loading = true
    this.progress = { processed: 0, total: 0, percentage: 0, isComplete: false }
    this.results = null
    this.selectedItems = new Set()
    this.filters = { fileType: '', folder: '', search: '' }
    this.sortBy = 'size'
    this.currentPage = 1
    this.itemsPerPage = 50
    this.totalItems = 0
    this.totalPages = 1
    this.error = null
    this.showConfirmDialog = false
    this.confirmDialogData = null
    this.pollInterval = null
    this.filterTimeout = null
    this.authHelper = new AuthenticationHelper(this)
    this._allItemSizes = new Map()
    this.selectedProfile = 'quick' // Default to Quick scan
    this.originalUnusedCount = 0 // Track unfiltered count to keep controls visible during search

    // Profile configurations
    this.profiles = [
      {
        id: 'quick',
        name: 'Quick Scan',
        description:
          'Scans only content items (media pickers, rich text editors)',
        duration: '5-10 seconds',
        useCase: 'Frequent scans and quick checks',
        icon: 'icon-flash',
      },
      {
        id: 'deep',
        name: 'Deep Scan',
        description: 'Scans content + Views folder + JavaScript + CSS',
        duration: '30-60 seconds',
        useCase: 'Regular maintenance and before cleanup',
        icon: 'icon-search',
      },
      {
        id: 'complete',
        name: 'Complete Scan',
        description:
          'Scans everything: content, Views, JS, CSS, wwwroot, TypeScript, SCSS, config files',
        duration: '2-5 minutes',
        useCase: 'Comprehensive audits and major cleanup',
        icon: 'icon-check',
      },
    ]
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
      await this.loadResults(true)
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

  async startScan() {
    try {
      this.error = null

      // Map profile ID to API parameter (0=Quick, 1=Deep, 2=Complete)
      const profileMap = { quick: 0, deep: 1, complete: 2 }
      const profileValue = profileMap[this.selectedProfile] || 0

      const url = `/umbraco/management/api/v1/umediaops/unused/start-scan?scanProfile=${profileValue}`

      const response = await this.makeAuthenticatedRequest(url, {
        method: 'POST',
      })

      if (!response.ok) {
        if (response.status === 429) {
          NotificationHelper.showWarning(
            this,
            'Please wait a few seconds before starting another scan.',
          )
          return
        }
        if (response.status === 400) {
          const data = await response.json().catch(() => ({}))
          NotificationHelper.showWarning(
            this,
            data.message || 'A scan is already in progress.',
          )
          return
        }
        throw new Error('Failed to start scan')
      }

      this.isScanning = true
      this.results = null
      this.selectedItems.clear()
      this.startPolling()

      const profileName =
        this.profiles.find((p) => p.id === this.selectedProfile)?.name ||
        'Quick Scan'
      NotificationHelper.showSuccess(
        this,
        `${profileName} started successfully`,
      )
    } catch (err) {
      this.error = err.message
      NotificationHelper.showError(this, `Failed to start scan: ${err.message}`)
    }
  }

  async cancelScan() {
    try {
      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/unused/scan/cancel',
        {
          method: 'POST',
        },
      )

      if (!response.ok) throw new Error('Failed to cancel scan')

      this.stopPolling()
      this.isScanning = false
      this.progress = {
        processed: 0,
        total: 0,
        percentage: 0,
        isComplete: false,
      }

      NotificationHelper.showSuccess(this, 'Scan cancelled successfully')
    } catch (err) {
      NotificationHelper.showError(
        this,
        `Failed to cancel scan: ${err.message}`,
      )
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
        '/umbraco/management/api/v1/umediaops/unused/scan/progress',
      )
      if (!response.ok) return

      const data = await response.json()
      this.progress = data.progress || data
      this.isScanning = data.isScanning

      if (data.progress && data.progress.isComplete) {
        this.stopPolling()
        this.isScanning = false // Ensure scanning state is false
        await this.loadResults()

        if (this.results) {
          NotificationHelper.showSuccess(
            this,
            `Scan completed! Found ${this.results.unusedCount} unused media items.`,
          )
        }
      }
    } catch (err) {
      // Silently handle errors
      this.stopPolling()
      this.isScanning = false // Stop scanning on error
    }
  }

  async loadResults(isInitialLoad = false) {
    try {
      // Only show full loading spinner on initial load, not on filter/search/pagination
      if (isInitialLoad || !this.results) {
        this.loading = true
      }
      const url = new URL(
        '/umbraco/management/api/v1/umediaops/unused/scan/results',
        window.location.origin,
      )

      // Add pagination
      url.searchParams.append('pageNumber', this.currentPage)
      url.searchParams.append('pageSize', this.itemsPerPage)

      // Add file type filter
      if (this.filters.fileType) {
        url.searchParams.append('fileType', this.filters.fileType)
      }

      // Add search filter
      if (this.filters.search) {
        url.searchParams.append('search', this.filters.search)
      }

      // Add sort parameter
      if (this.sortBy) {
        url.searchParams.append('sortBy', this.sortBy)
      }

      const response = await this.makeAuthenticatedRequest(url)

      if (response.status === 404) {
        this.results = null
        this.loading = false
        return
      }

      if (!response.ok) {
        // Silently handle other errors - don't log to console
        this.results = null
        this.loading = false
        return
      }

      const data = await response.json()

      // Backend now returns paginated response
      if (data && data.totalScanned !== undefined) {
        this.results = {
          scanId: data.scanId,
          scannedAt: data.scannedAt,
          totalScanned: data.totalScanned,
          unusedCount: data.unusedCount,
          totalStorageWasted: data.totalSize,
          unusedItems: data.items || [],
          // Scan profile and duration
          profile: data.profile,
          profileName: data.profileName,
          durationSeconds: data.durationSeconds,
          itemsWithCodeReferences: data.itemsWithCodeReferences || 0,
          // File type breakdown
          contentItemsScanned: data.contentItemsScanned || 0,
          viewFilesScanned: data.viewFilesScanned || 0,
          templatesScanned: data.templatesScanned || 0,
          partialViewsScanned: data.partialViewsScanned || 0,
          blockComponentsScanned: data.blockComponentsScanned || 0,
          layoutsScanned: data.layoutsScanned || 0,
          javaScriptFilesScanned: data.javaScriptFilesScanned || 0,
          cssFilesScanned: data.cssFilesScanned || 0,
          typeScriptFilesScanned: data.typeScriptFilesScanned || 0,
          scssFilesScanned: data.scssFilesScanned || 0,
          configFilesScanned: data.configFilesScanned || 0,
          wwwrootFilesScanned: data.wwwrootFilesScanned || 0,
        }
        this.totalItems = data.totalItems || 0
        this.totalPages = data.totalPages || 1

        // Track original unused count (without filters) to keep controls visible during search
        const hasFilters = this.filters.search || this.filters.fileType
        if (!hasFilters) {
          this.originalUnusedCount = data.unusedCount || 0
        }
      } else {
        // Invalid scan results data received
        this.results = null
      }
    } catch (err) {
      // Silently handle errors - don't log 404s or other errors to console
      this.results = null
    } finally {
      this.loading = false
    }
  }

  // Server-side pagination - no client-side filtering needed
  getPaginatedItems() {
    return this.results?.unusedItems || []
  }

  getTotalPages() {
    return this.totalPages || 1
  }

  toggleSelection(mediaId) {
    if (this.selectedItems.has(mediaId)) {
      this.selectedItems.delete(mediaId)
    } else {
      this.selectedItems.add(mediaId)
    }
    this.selectedItems = new Set(this.selectedItems)
  }

  selectAll() {
    // Clear existing selection and select only current page
    this.selectedItems = new Set()
    const items = this.getPaginatedItems()
    items.forEach((item) => this.selectedItems.add(item.mediaId))
    this.selectedItems = new Set(this.selectedItems)
  }

  async selectAllItems() {
    NotificationHelper.showInfo(
      this,
      `Selecting all ${this.totalItems} items across all pages...`,
    )

    // Load all pages and collect all items with their sizes
    this._allItemSizes = new Map()
    const totalPages = this.totalPages
    for (let page = 1; page <= totalPages; page++) {
      const url = new URL(
        '/umbraco/management/api/v1/umediaops/unused/scan/results',
        window.location.origin,
      )
      url.searchParams.append('pageNumber', page)
      url.searchParams.append('pageSize', this.itemsPerPage)
      if (this.filters.fileType)
        url.searchParams.append('fileType', this.filters.fileType)
      if (this.filters.search)
        url.searchParams.append('search', this.filters.search)
      if (this.sortBy) url.searchParams.append('sortBy', this.sortBy)

      const response = await this.makeAuthenticatedRequest(url)
      if (response.ok) {
        const data = await response.json()
        for (const item of data.items || []) {
          this.selectedItems.add(item.mediaId)
          this._allItemSizes.set(item.mediaId, item.fileSize)
        }
      }
    }

    this.selectedItems = new Set(this.selectedItems)
    NotificationHelper.showSuccess(
      this,
      `Selected all ${this.selectedItems.size} items`,
    )
  }

  clearSelection() {
    this.selectedItems = new Set()
    this._allItemSizes = new Map()
    this.requestUpdate()
  }

  openConfirmDialog(type, data) {
    this.confirmDialogData = { type, ...data }
    this.showConfirmDialog = true
  }

  closeConfirmDialog() {
    this.showConfirmDialog = false
    this.confirmDialogData = null
  }

  calculateSpaceToFree() {
    let total = 0

    // Build a lookup from current page items
    const currentPageSizes = new Map()
    for (const item of this.results?.unusedItems || []) {
      currentPageSizes.set(item.mediaId, item.fileSize)
    }

    for (const mediaId of this.selectedItems) {
      // Try current page first, then cached sizes from selectAllItems
      if (currentPageSizes.has(mediaId)) {
        total += currentPageSizes.get(mediaId)
      } else if (this._allItemSizes?.has(mediaId)) {
        total += this._allItemSizes.get(mediaId)
      }
    }
    return total
  }

  async confirmDelete() {
    try {
      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/unused/delete',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ mediaIds: Array.from(this.selectedItems) }),
        },
      )

      if (!response.ok) throw new Error('Failed to delete media items')

      const result = await response.json()

      this.closeConfirmDialog()
      this.selectedItems = new Set() // Create new Set to trigger reactivity

      // Reload results first
      await this.loadResults()

      // Force a re-render
      this.requestUpdate()

      // Then show notifications
      if (result.successCount > 0) {
        await NotificationHelper.showSuccess(
          this,
          `Successfully deleted ${result.successCount} items`,
        )
      }

      if (result.failureCount > 0) {
        await NotificationHelper.showError(
          this,
          `Failed to delete ${result.failureCount} items`,
        )
      }
    } catch (err) {
      await NotificationHelper.showError(this, `Error: ${err.message}`)
      this.closeConfirmDialog()
    }
  }

  async exportToCsv() {
    try {
      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/unused/export',
      )

      if (!response.ok) throw new Error('Failed to export results')

      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `unused-media-${new Date().toISOString().split('T')[0]}.csv`
      a.click()

      // Revoke URL after a delay to ensure download starts
      setTimeout(() => URL.revokeObjectURL(url), 100)

      await NotificationHelper.showSuccess(
        this,
        'Results exported successfully',
      )
    } catch (err) {
      await NotificationHelper.showError(
        this,
        `Failed to export: ${err.message}`,
      )
    }
  }

  async bulkDownload() {
    if (this.selectedItems.size === 0) {
      await NotificationHelper.showWarning(
        this,
        'Please select items to download',
      )
      return
    }

    try {
      await NotificationHelper.showInfo(
        this,
        `Preparing download of ${this.selectedItems.size} file(s)...`,
      )

      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/unused/download',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ mediaIds: Array.from(this.selectedItems) }),
        },
      )

      if (!response.ok) throw new Error('Failed to download files')

      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `unused-media-backup-${new Date().toISOString().split('T')[0]}.zip`
      a.click()

      setTimeout(() => URL.revokeObjectURL(url), 100)

      await NotificationHelper.showSuccess(
        this,
        'Files downloaded successfully',
      )
    } catch (err) {
      await NotificationHelper.showError(
        this,
        `Failed to download: ${err.message}`,
      )
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
    if (!dateString) return ''
    let utcDate = dateString
    if (
      !utcDate.endsWith('Z') &&
      !utcDate.includes('+') &&
      !utcDate.includes('-', 10)
    ) {
      utcDate += 'Z'
    }
    return new Date(utcDate).toLocaleString()
  }

  formatTimeAgo(dateString) {
    if (!dateString) return 'Never'

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

    if (seconds < 0) return 'Just now'
    if (seconds < 60) return 'Just now'
    if (seconds < 3600) return `${Math.floor(seconds / 60)} minutes ago`
    if (seconds < 86400) return `${Math.floor(seconds / 3600)} hours ago`
    if (seconds < 604800) return `${Math.floor(seconds / 86400)} days ago`

    return date.toLocaleDateString()
  }

  isImageType(fileType) {
    const imageTypes = ['Image', 'image', 'umbracoMediaVectorGraphics']
    return (
      imageTypes.includes(fileType) || fileType.toLowerCase().includes('image')
    )
  }

  getFileIcon(fileType) {
    const type = fileType.toLowerCase()
    if (type.includes('image')) return 'icon-picture'
    if (type.includes('video')) return 'icon-video'
    if (type.includes('audio')) return 'icon-music'
    if (type.includes('pdf') || type.includes('document'))
      return 'icon-document'
    return 'icon-file'
  }

  getFileExtension(fileName) {
    if (!fileName) return ''
    const parts = fileName.split('.')
    if (parts.length < 2) return ''
    return parts[parts.length - 1].toUpperCase()
  }

  getExtensionColor(extension) {
    const ext = extension.toLowerCase()
    // Documents
    if (['pdf'].includes(ext)) return '#e74c3c'
    if (['doc', 'docx'].includes(ext)) return '#2980b9'
    if (['xls', 'xlsx'].includes(ext)) return '#27ae60'
    if (['ppt', 'pptx'].includes(ext)) return '#e67e22'
    // Media
    if (['mp4', 'avi', 'mov', 'wmv'].includes(ext)) return '#9b59b6'
    if (['mp3', 'wav', 'ogg'].includes(ext)) return '#1abc9c'
    // Archives
    if (['zip', 'rar', '7z'].includes(ext)) return '#f39c12'
    // Code/Text
    if (['txt', 'json', 'xml', 'csv'].includes(ext)) return '#34495e'
    // Default
    return '#95a5a6'
  }

  async goToPage(page) {
    this.currentPage = page
    await this.loadResults()
  }

  async nextPage() {
    if (this.currentPage < this.getTotalPages()) {
      this.currentPage++
      await this.loadResults()
    }
  }

  async previousPage() {
    if (this.currentPage > 1) {
      this.currentPage--
      await this.loadResults()
    }
  }

  getConfirmDialogContent() {
    if (!this.confirmDialogData) return null

    const { type, count } = this.confirmDialogData

    if (type === 'delete') {
      return {
        title: 'Confirm Deletion',
        message: `Are you sure you want to delete ${count} unused media file(s)? This will free up ${this.formatBytes(this.calculateSpaceToFree())} of storage.`,
        warning: 'Files will be moved to the recycle bin and can be restored.',
        confirmLabel: 'Delete Files',
        confirmColor: 'warning',
        onConfirm: () => this.confirmDelete(),
      }
    }

    return null
  }

  render() {
    const paginatedItems = this.getPaginatedItems()
    const totalPages = this.getTotalPages()

    return html`
      <div class="dashboard-container">
        ${this.loading
          ? html`
              <div class="loading">
                <uui-loader></uui-loader>
                <p>Loading unused media data...</p>
              </div>
            `
          : html`
              <div class="header">
                <div class="title-section">
                  <h1>
                    <uui-icon name="icon-trash"></uui-icon>
                    Unused Media Finder
                  </h1>
                  <span class="package-badge">uMediaOps</span>
                </div>
                <p class="subtitle">
                  Identify and remove media files that are not referenced in
                  your content
                </p>
              </div>

              ${this.error
                ? html`
                    <uui-box>
                      <p style="color: var(--uui-color-danger);">
                        ${this.error}
                      </p>
                    </uui-box>
                  `
                : ''}

              <uui-box class="scan-box">
                <div class="scan-content">
                  <div class="scan-info">
                    <h2>
                      <uui-icon name="icon-search"></uui-icon>
                      Scan for Unused Media
                    </h2>
                    <p class="description">
                      Choose a scan profile based on how thorough you want the
                      scan to be.
                    </p>

                    <div class="scan-options">
                      ${this.profiles.map(
                        (profile) => html`
                          <label
                            class="profile-option ${this.selectedProfile ===
                            profile.id
                              ? 'selected'
                              : ''}"
                          >
                            <input
                              type="radio"
                              name="scanProfile"
                              value="${profile.id}"
                              .checked=${this.selectedProfile === profile.id}
                              @change=${() => {
                                this.selectedProfile = profile.id
                              }}
                              ?disabled=${this.isScanning}
                            />
                            <div class="profile-content">
                              <div class="profile-header">
                                <uui-icon name="${profile.icon}"></uui-icon>
                                <strong>${profile.name}</strong>
                                <span class="profile-duration"
                                  >${profile.duration}</span
                                >
                              </div>
                              <p class="profile-description">
                                ${profile.description}
                              </p>
                              <p class="profile-usecase">
                                <em>Best for: ${profile.useCase}</em>
                              </p>
                            </div>
                          </label>
                        `,
                      )}
                    </div>

                    <div class="button-group">
                      <uui-button
                        look="primary"
                        color="positive"
                        label="Start Scan"
                        @click=${this.startScan}
                        ?disabled=${this.isScanning}
                        class="scan-button-large"
                        aria-label="Start scanning for unused media"
                      >
                        <uui-icon name="icon-refresh"></uui-icon>
                        ${this.isScanning ? 'Scanning...' : 'Start Scan'}
                      </uui-button>

                      ${this.isScanning
                        ? html`
                            <uui-button
                              look="outline"
                              color="warning"
                              label="Cancel Scan"
                              @click=${this.cancelScan}
                              class="scan-button-large"
                              aria-label="Cancel the current scan"
                            >
                              <uui-icon name="icon-delete"></uui-icon>
                              Cancel Scan
                            </uui-button>
                          `
                        : ''}
                    </div>
                  </div>

                  ${this.isScanning
                    ? html`
                        <div class="progress-section" aria-busy="true">
                          <div
                            role="status"
                            aria-live="polite"
                            aria-atomic="true"
                            class="sr-only"
                          >
                            Scanning in progress: ${this.progress.percentage}%
                            complete.
                          </div>
                          <div class="progress-stats">
                            <span class="progress-text">
                              Processing: ${this.progress.processed} /
                              ${this.progress.total}
                            </span>
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

              ${this.results
                ? this.renderResults(paginatedItems, totalPages)
                : ''}
              ${this.showConfirmDialog && this.confirmDialogData
                ? html`
                    <div
                      class="dialog-overlay"
                      @click=${this.closeConfirmDialog}
                    >
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
            `}
      </div>
    `
  }

  renderFileTypeBreakdown() {
    if (!this.results) return ''

    const breakdown = {
      contentItemsScanned: this.results.contentItemsScanned || 0,
      viewFilesScanned: this.results.viewFilesScanned || 0,
      templatesScanned: this.results.templatesScanned || 0,
      partialViewsScanned: this.results.partialViewsScanned || 0,
      blockComponentsScanned: this.results.blockComponentsScanned || 0,
      layoutsScanned: this.results.layoutsScanned || 0,
      javaScriptFilesScanned: this.results.javaScriptFilesScanned || 0,
      cssFilesScanned: this.results.cssFilesScanned || 0,
      typeScriptFilesScanned: this.results.typeScriptFilesScanned || 0,
      scssFilesScanned: this.results.scssFilesScanned || 0,
      configFilesScanned: this.results.configFilesScanned || 0,
      wwwrootFilesScanned: this.results.wwwrootFilesScanned || 0,
    }

    // Always show breakdown if we have any scan data
    const hasScanData = breakdown.contentItemsScanned > 0

    if (!hasScanData) return ''

    return html`
      <div class="file-type-breakdown">
        <h3>
          <uui-icon name="icon-folder"></uui-icon>
          Files Scanned by Type
        </h3>
        <ul class="breakdown-list">
          ${breakdown.contentItemsScanned > 0
            ? html`
                <li class="breakdown-item">
                  <span class="breakdown-icon">📄</span>
                  <span class="breakdown-label">Content items checked</span>
                  <span class="breakdown-count"
                    >${breakdown.contentItemsScanned}</span
                  >
                </li>
              `
            : ''}
          ${breakdown.viewFilesScanned > 0
            ? html`
                <li class="breakdown-item">
                  <span class="breakdown-icon">📝</span>
                  <span class="breakdown-label"
                    >View files scanned (.cshtml)</span
                  >
                  <span class="breakdown-count"
                    >${breakdown.viewFilesScanned}</span
                  >
                </li>
                ${breakdown.templatesScanned > 0 ||
                breakdown.partialViewsScanned > 0 ||
                breakdown.blockComponentsScanned > 0 ||
                breakdown.layoutsScanned > 0
                  ? html`
                      <ul class="breakdown-sublist">
                        ${breakdown.templatesScanned > 0
                          ? html`<li>
                              ├─ ${breakdown.templatesScanned} templates
                            </li>`
                          : ''}
                        ${breakdown.partialViewsScanned > 0
                          ? html`<li>
                              ├─ ${breakdown.partialViewsScanned} partial views
                            </li>`
                          : ''}
                        ${breakdown.blockComponentsScanned > 0
                          ? html`<li>
                              ├─ ${breakdown.blockComponentsScanned} block
                              components
                            </li>`
                          : ''}
                        ${breakdown.layoutsScanned > 0
                          ? html`<li>
                              └─ ${breakdown.layoutsScanned} layouts
                            </li>`
                          : ''}
                      </ul>
                    `
                  : ''}
              `
            : ''}
          ${breakdown.javaScriptFilesScanned > 0
            ? html`
                <li class="breakdown-item">
                  <span class="breakdown-icon">⚡</span>
                  <span class="breakdown-label">JavaScript files scanned</span>
                  <span class="breakdown-count"
                    >${breakdown.javaScriptFilesScanned}</span
                  >
                </li>
              `
            : ''}
          ${breakdown.cssFilesScanned > 0
            ? html`
                <li class="breakdown-item">
                  <span class="breakdown-icon">🎨</span>
                  <span class="breakdown-label">CSS files scanned</span>
                  <span class="breakdown-count"
                    >${breakdown.cssFilesScanned}</span
                  >
                </li>
              `
            : ''}
          ${breakdown.typeScriptFilesScanned > 0
            ? html`
                <li class="breakdown-item">
                  <span class="breakdown-icon">📘</span>
                  <span class="breakdown-label">TypeScript files scanned</span>
                  <span class="breakdown-count"
                    >${breakdown.typeScriptFilesScanned}</span
                  >
                </li>
              `
            : ''}
          ${breakdown.scssFilesScanned > 0
            ? html`
                <li class="breakdown-item">
                  <span class="breakdown-icon">💅</span>
                  <span class="breakdown-label">SCSS/LESS files scanned</span>
                  <span class="breakdown-count"
                    >${breakdown.scssFilesScanned}</span
                  >
                </li>
              `
            : ''}
          ${breakdown.configFilesScanned > 0
            ? html`
                <li class="breakdown-item">
                  <span class="breakdown-icon">⚙️</span>
                  <span class="breakdown-label">Config files scanned</span>
                  <span class="breakdown-count"
                    >${breakdown.configFilesScanned}</span
                  >
                </li>
              `
            : ''}
          ${breakdown.wwwrootFilesScanned > 0
            ? html`
                <li class="breakdown-item">
                  <span class="breakdown-icon">🌐</span>
                  <span class="breakdown-label">Wwwroot files scanned</span>
                  <span class="breakdown-count"
                    >${breakdown.wwwrootFilesScanned}</span
                  >
                </li>
              `
            : ''}
        </ul>
      </div>
    `
  }

  formatDuration(durationSeconds) {
    if (!durationSeconds) return '0s'
    if (durationSeconds < 60) {
      return `${Math.round(durationSeconds)}s`
    }
    const minutes = Math.floor(durationSeconds / 60)
    const seconds = Math.round(durationSeconds % 60)
    return `${minutes}m ${seconds}s`
  }

  renderResults(paginatedItems, totalPages) {
    return html`
      <div class="results-section">
        <h2 class="results-title">
          <uui-icon name="icon-chart"></uui-icon>
          Scan Results
        </h2>

        ${this.results.scannedAt
          ? html`
              <div class="scan-info-bar">
                <span>
                  <uui-icon name="icon-calendar"></uui-icon>
                  Last scan: ${this.formatTimeAgo(this.results.scannedAt)}
                </span>
                ${this.results.profileName
                  ? html`
                      <span>
                        <uui-icon name="icon-settings"></uui-icon>
                        Profile: ${this.results.profileName}
                      </span>
                    `
                  : ''}
                ${this.results.durationSeconds
                  ? html`
                      <span>
                        <uui-icon name="icon-time"></uui-icon>
                        Duration:
                        ${this.formatDuration(this.results.durationSeconds)}
                      </span>
                    `
                  : ''}
              </div>
            `
          : ''}

        <div class="results-grid">
          <div class="result-card">
            <uui-icon name="icon-files" class="card-icon"></uui-icon>
            <div class="result-value">${this.results.totalScanned}</div>
            <div class="result-label">Media Files Scanned</div>
          </div>
          <div class="result-card highlight">
            <uui-icon name="icon-trash" class="card-icon"></uui-icon>
            <div class="result-value">${this.results.unusedCount}</div>
            <div class="result-label">Unused Files</div>
          </div>
          <div class="result-card highlight">
            <uui-icon name="icon-box" class="card-icon"></uui-icon>
            <div class="result-value">
              ${this.formatBytes(this.results.totalStorageWasted)}
            </div>
            <div class="result-label">Storage Wasted</div>
          </div>
          ${this.results.itemsWithCodeReferences > 0
            ? html`
                <div class="result-card warning">
                  <uui-icon name="icon-alert" class="card-icon"></uui-icon>
                  <div class="result-value">
                    ${this.results.itemsWithCodeReferences}
                  </div>
                  <div class="result-label">Files with Code References</div>
                  <div class="scan-details">
                    <small>Manual review recommended</small>
                  </div>
                </div>
              `
            : ''}
        </div>

        ${this.renderFileTypeBreakdown()}
        ${this.originalUnusedCount > 0
          ? html`
              <div class="controls">
                <div class="filters">
                  <div class="filter-group search-group">
                    <label for="searchFilter">Search:</label>
                    <input
                      id="searchFilter"
                      type="text"
                      placeholder="Search by filename..."
                      autocomplete="off"
                      .value=${this.filters.search || ''}
                      @input=${(e) => {
                        this.filters.search = e.target.value
                        clearTimeout(this.filterTimeout)
                        this.filterTimeout = setTimeout(async () => {
                          this.currentPage = 1
                          await this.loadResults()
                        }, 500)
                      }}
                      aria-label="Search by filename"
                    />
                  </div>

                  <div class="filter-group">
                    <label for="fileTypeFilter">Filter by type:</label>
                    <input
                      id="fileTypeFilter"
                      type="text"
                      placeholder="e.g., Image, PDF, MP4, DOCX"
                      autocomplete="off"
                      @input=${(e) => {
                        this.filters.fileType = e.target.value
                        // Debounce filter - wait for user to stop typing
                        clearTimeout(this.filterTimeout)
                        this.filterTimeout = setTimeout(async () => {
                          this.currentPage = 1
                          await this.loadResults()
                        }, 500)
                      }}
                      aria-label="Filter by file type or extension"
                    />
                  </div>

                  <div class="filter-group">
                    <label for="sortBy">Sort by:</label>
                    <select
                      id="sortBy"
                      .value=${this.sortBy}
                      @change=${async (e) => {
                        this.sortBy = e.target.value
                        this.currentPage = 1
                        await this.loadResults()
                      }}
                      aria-label="Sort results by"
                    >
                      <option value="size">Size (largest first)</option>
                      <option value="date">Date (newest first)</option>
                      <option value="name">Name (A-Z)</option>
                    </select>
                  </div>
                </div>

                <uui-button
                  look="outline"
                  label="Select Page"
                  @click=${this.selectAll}
                  ?disabled=${paginatedItems.length === 0}
                >
                  <uui-icon name="icon-check"></uui-icon>
                  Select Page
                </uui-button>
                <uui-button
                  look="primary"
                  color="positive"
                  label="Select All Items"
                  @click=${this.selectAllItems}
                  ?disabled=${this.totalItems === 0}
                >
                  <uui-icon name="icon-check"></uui-icon>
                  Select All (${this.totalItems})
                </uui-button>
                <uui-button
                  look="outline"
                  label="Export CSV"
                  @click=${this.exportToCsv}
                >
                  <uui-icon name="icon-download"></uui-icon>
                  Export CSV
                </uui-button>
              </div>

              ${this.selectedItems.size > 0
                ? html`
                    <div class="bulk-actions">
                      <div class="bulk-actions-info">
                        <span class="selection-count"
                          >${this.selectedItems.size}
                          item${this.selectedItems.size > 1 ? 's' : ''}
                          selected</span
                        >
                        <span class="selection-size"
                          >Original size:
                          ${this.formatBytes(this.calculateSpaceToFree())}</span
                        >
                      </div>
                      <div class="bulk-actions-buttons">
                        <uui-button
                          look="primary"
                          color="positive"
                          label="Download Selected"
                          @click=${this.bulkDownload}
                        >
                          <uui-icon name="icon-download"></uui-icon>
                          Download ZIP
                        </uui-button>
                        <uui-button
                          look="primary"
                          color="warning"
                          label="Delete Selected"
                          @click=${() =>
                            this.openConfirmDialog('delete', {
                              count: this.selectedItems.size,
                            })}
                        >
                          <uui-icon name="icon-trash"></uui-icon>
                          Delete Selected
                        </uui-button>
                        <uui-button
                          look="outline"
                          label="Clear Selection"
                          @click=${this.clearSelection}
                          >Clear</uui-button
                        >
                      </div>
                    </div>
                  `
                : ''}

              <div class="summary">
                <strong>${this.totalItems}</strong>
                item${this.totalItems !== 1 ? 's' : ''}
                ${totalPages > 1
                  ? html` (Page ${this.currentPage} of ${totalPages})`
                  : ''}
              </div>

              <div class="items-list">
                ${paginatedItems.length === 0
                  ? html`
                      <uui-box>
                        <div
                          style="text-align: center; padding: 24px; color: var(--uui-color-text-alt);"
                        >
                          <uui-icon
                            name="icon-search"
                            style="font-size: 2rem;"
                          ></uui-icon>
                          <p>No items match your search or filter criteria.</p>
                        </div>
                      </uui-box>
                    `
                  : ''}
                ${paginatedItems.map(
                  (item) => html`
                    <uui-box class="item-card">
                      <div class="item-header">
                        <input
                          type="checkbox"
                          role="checkbox"
                          aria-checked="${this.selectedItems.has(item.mediaId)}"
                          aria-label="Select ${item.fileName}"
                          .checked=${this.selectedItems.has(item.mediaId)}
                          @change=${() => this.toggleSelection(item.mediaId)}
                        />
                        <div class="item-preview">
                          ${this.isImageType(item.fileType)
                            ? html`<img
                                src="${item.filePath}?width=60&height=60&mode=crop"
                                alt="${item.fileName}"
                                class="preview-img"
                              />`
                            : html`
                                <div class="file-preview-container">
                                  <uui-icon
                                    name="${this.getFileIcon(item.fileType)}"
                                    class="preview-icon"
                                  ></uui-icon>
                                  ${this.getFileExtension(item.fileName)
                                    ? html`
                                        <div
                                          class="file-extension-badge"
                                          style="background-color: ${this.getExtensionColor(
                                            this.getFileExtension(
                                              item.fileName,
                                            ),
                                          )};"
                                        >
                                          ${this.getFileExtension(
                                            item.fileName,
                                          )}
                                        </div>
                                      `
                                    : ''}
                                </div>
                              `}
                        </div>
                        <div class="item-info">
                          <div class="item-name">
                            <strong>${item.fileName}</strong>
                          </div>
                          <div class="item-meta">
                            <span>${this.formatBytes(item.fileSize)}</span>
                            <span class="separator">•</span>
                            <span>${item.fileType}</span>
                            <span class="separator">•</span>
                            <span>${this.formatDate(item.uploadDate)}</span>
                          </div>
                          <div class="item-path" title="${item.filePath}">
                            ${item.filePath}
                          </div>
                        </div>
                      </div>
                    </uui-box>
                  `,
                )}
              </div>

              ${totalPages > 1
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
                          { length: Math.min(totalPages, 10) },
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
                        ?disabled=${this.currentPage === totalPages}
                      >
                        Next
                        <uui-icon name="icon-arrow-right"></uui-icon>
                      </uui-button>
                    </div>
                  `
                : ''}
            `
          : html`
              <div class="success-message">
                <uui-icon name="icon-check"></uui-icon>
                <div>
                  <strong>Great news!</strong> No unused media files were found
                  in your library.
                </div>
              </div>
            `}
      </div>
    `
  }

  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    .loading {
      text-align: center;
      padding: var(--uui-size-space-6);
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

    .scan-box {
      margin-bottom: 16px;
    }

    .scan-content {
      padding: 24px;
    }

    .scan-info h2 {
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

    .recommendation-banner {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--uui-size-space-3);
      padding: 16px;
      background: linear-gradient(135deg, #3b82f615 0%, #2563eb15 100%);
      border: 2px solid #3b82f6;
      border-radius: var(--uui-border-radius);
      margin-bottom: var(--uui-size-space-4);
    }

    .recommendation-content {
      display: flex;
      align-items: flex-start;
      gap: var(--uui-size-space-3);
      flex: 1;
    }

    .recommendation-content uui-icon {
      font-size: 1.5rem;
      color: #3b82f6;
      flex-shrink: 0;
      margin-top: 2px;
    }

    .recommendation-text {
      flex: 1;
    }

    .recommendation-text strong {
      display: block;
      color: var(--uui-color-text);
      margin-bottom: 4px;
      font-size: 1rem;
    }

    .recommendation-text p {
      margin: 0;
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
      line-height: 1.5;
    }

    .scan-options {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 12px;
      margin: var(--uui-size-space-4) 0;
    }

    .profile-option {
      display: flex;
      align-items: flex-start;
      gap: 10px;
      padding: 12px;
      background: var(--uui-color-surface-alt);
      border: 2px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      cursor: pointer;
      transition: all 0.2s ease;
    }

    .profile-option:hover {
      border-color: #00b5a3;
      background: var(--uui-color-surface);
    }

    .profile-option.selected {
      border-color: #00b5a3;
      background: linear-gradient(135deg, #00b5a315 0%, #1e293b15 100%);
      box-shadow: 0 2px 8px rgba(0, 181, 163, 0.2);
    }

    .profile-option input[type='radio'] {
      width: 18px;
      height: 18px;
      cursor: pointer;
      margin-top: 1px;
      flex-shrink: 0;
      accent-color: #00b5a3;
    }

    .profile-option input[type='radio']:disabled {
      cursor: not-allowed;
      opacity: 0.5;
    }

    .profile-content {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .profile-header {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .profile-header uui-icon {
      font-size: 1rem;
      color: #00b5a3;
    }

    .profile-header strong {
      font-size: 0.95rem;
      color: var(--uui-color-text);
    }

    .profile-duration {
      margin-left: auto;
      padding: 2px 8px;
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: 10px;
      font-size: 0.75rem;
      color: var(--uui-color-text-alt);
      font-weight: 500;
      white-space: nowrap;
    }

    .profile-description {
      margin: 0;
      color: var(--uui-color-text-alt);
      font-size: 0.8rem;
      line-height: 1.4;
    }

    .profile-usecase {
      display: none;
    }

    @media (max-width: 900px) {
      .scan-options {
        grid-template-columns: 1fr;
      }
    }

    .checkbox-label {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      cursor: pointer;
      user-select: none;
    }

    .checkbox-label input[type='checkbox'] {
      width: 20px;
      height: 20px;
      cursor: pointer;
      margin-top: 2px;
      flex-shrink: 0;
    }

    .checkbox-label input[type='checkbox']:disabled {
      cursor: not-allowed;
      opacity: 0.5;
    }

    .checkbox-text {
      display: flex;
      flex-direction: column;
      gap: 6px;
      flex: 1;
    }

    .checkbox-text strong {
      font-size: 1rem;
      color: var(--uui-color-text);
    }

    .checkbox-description {
      font-size: 0.9rem;
      color: var(--uui-color-text-alt);
      line-height: 1.5;
      display: block;
    }

    .warning-text {
      display: block;
      margin-top: 4px;
      color: #f59e0b;
      font-weight: 500;
      font-size: 0.85rem;
    }

    .button-group {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
      justify-content: center;
      margin-top: var(--uui-size-space-5);
    }

    .scan-button-large {
      font-size: 1.25rem;
      padding: var(--uui-size-space-5) var(--uui-size-space-8);
      min-width: 200px;
      min-height: 60px;
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

    .results-section {
      padding: 24px;
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
    }

    .results-title {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      margin-top: 0;
      margin-bottom: var(--uui-size-space-5);
    }

    .scan-info-bar {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-4);
      padding: 12px 16px;
      background: var(--uui-color-surface-alt);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      margin-bottom: 16px;
      font-size: 0.9rem;
      color: var(--uui-color-text-alt);
    }

    .scan-info-bar span {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .scan-info-bar uui-icon {
      font-size: 1rem;
    }

    .results-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
      gap: 16px;
      margin-bottom: 16px;
    }

    .result-card {
      background: var(--uui-color-surface-alt);
      border: 2px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      padding: 24px;
      text-align: center;
    }

    .result-card.highlight {
      background: linear-gradient(135deg, #00b5a315 0%, #1e293b15 100%);
      border-color: #00b5a3;
    }

    .result-card.warning {
      background: linear-gradient(135deg, #f59e0b15 0%, #f97316 15 100%);
      border-color: #f59e0b;
    }

    .result-card.warning .card-icon {
      color: #f59e0b;
    }

    .result-card.warning .result-value {
      color: #f59e0b;
    }

    .card-icon {
      font-size: 2rem;
      color: var(--uui-color-text-alt);
      margin-bottom: var(--uui-size-space-2);
    }

    .result-card.highlight .card-icon {
      color: #00b5a3;
    }

    .result-value {
      font-size: 2rem;
      font-weight: bold;
      color: var(--uui-color-text);
      margin-bottom: var(--uui-size-space-2);
    }

    .result-card.highlight .result-value {
      color: #00b5a3;
    }

    .result-label {
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
      font-weight: 500;
    }

    .scan-details {
      margin-top: 12px;
      padding-top: 12px;
      border-top: 1px solid var(--uui-color-border);
      color: var(--uui-color-text-alt);
      font-size: 0.85rem;
      line-height: 1.6;
    }

    .scan-details small {
      display: block;
    }

    .file-type-breakdown {
      margin: 24px 0;
      padding: 20px;
      background: var(--uui-color-surface-alt);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
    }

    .file-type-breakdown h3 {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      margin: 0 0 16px 0;
      font-size: 1.1rem;
      color: var(--uui-color-text);
    }

    .breakdown-list {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .breakdown-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
    }

    .breakdown-icon {
      font-size: 1.5rem;
      flex-shrink: 0;
    }

    .breakdown-label {
      flex: 1;
      font-size: 0.95rem;
      color: var(--uui-color-text);
    }

    .breakdown-count {
      font-weight: 600;
      font-size: 1rem;
      color: #00b5a3;
      padding: 4px 12px;
      background: linear-gradient(135deg, #00b5a315 0%, #1e293b15 100%);
      border-radius: 12px;
    }

    .breakdown-sublist {
      list-style: none;
      padding: 8px 0 0 48px;
      margin: 8px 0 0 0;
      font-size: 0.9rem;
      color: var(--uui-color-text-alt);
      line-height: 1.8;
    }

    .breakdown-sublist li {
      font-family: 'Courier New', monospace;
    }

    .controls {
      display: flex;
      gap: 12px;
      margin-bottom: 16px;
      flex-wrap: wrap;
      padding: 16px;
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      border: 1px solid var(--uui-color-border);
      align-items: flex-end;
    }

    .filters {
      display: flex;
      gap: 16px;
      flex: 1;
      flex-wrap: wrap;
    }

    .filter-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-width: 200px;
    }

    .filter-group.search-group {
      flex: 1;
      min-width: 250px;
    }

    .filter-group label {
      font-size: 0.9rem;
      font-weight: 500;
      color: var(--uui-color-text);
    }

    .filter-group input,
    .filter-group select {
      padding: 10px 12px;
      border: 2px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface);
      color: var(--uui-color-text);
      font-size: 0.95rem;
      font-family: inherit;
    }

    .bulk-actions {
      display: flex;
      flex-direction: column;
      gap: 12px;
      padding: 16px;
      background: linear-gradient(135deg, #00b5a315 0%, #1e293b15 100%);
      border: 2px solid #00b5a3;
      border-radius: var(--uui-border-radius);
      margin-bottom: 16px;
    }

    .bulk-actions-info {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-4);
      flex-wrap: wrap;
    }

    .selection-count {
      font-weight: 600;
      font-size: 1.05rem;
      color: var(--uui-color-text);
    }

    .selection-size {
      font-weight: 500;
      color: #00b5a3;
      font-size: 0.95rem;
    }

    .bulk-actions-buttons {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
    }

    .bulk-actions span {
      flex: 1;
      font-weight: 500;
    }

    .summary {
      margin-bottom: 16px;
      padding: 16px;
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
    }

    .items-list {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .item-card {
      transition:
        transform 0.2s,
        box-shadow 0.2s;
      border: 1px solid var(--uui-color-border);
    }

    .item-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
    }

    .item-header {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
    }

    .item-header input[type='checkbox'] {
      width: 20px;
      height: 20px;
      cursor: pointer;
    }

    .item-preview {
      width: 60px;
      height: 60px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--uui-color-surface-alt);
      border-radius: 4px;
      flex-shrink: 0;
      position: relative;
    }

    .file-preview-container {
      width: 100%;
      height: 100%;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 4px;
    }

    .preview-img {
      width: 60px;
      height: 60px;
      object-fit: cover;
      border-radius: 4px;
    }

    .preview-icon {
      font-size: 28px;
      color: var(--uui-color-text-alt);
    }

    .file-extension-badge {
      font-size: 0.65rem;
      font-weight: 700;
      color: white;
      padding: 2px 6px;
      border-radius: 3px;
      text-align: center;
      letter-spacing: 0.3px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
      min-width: 32px;
    }

    .item-info {
      flex: 1;
      min-width: 0;
    }

    .item-name {
      margin-bottom: var(--uui-size-space-1);
      font-size: 1.1rem;
    }

    .item-meta {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      font-size: 0.9rem;
      color: var(--uui-color-text-alt);
      margin-bottom: var(--uui-size-space-1);
    }

    .item-path {
      font-size: 0.85rem;
      color: var(--uui-color-text-alt);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .separator {
      color: var(--uui-color-border);
    }

    .pagination {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-top: 16px;
      padding: 16px;
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      border: 1px solid var(--uui-color-border);
    }

    .page-numbers {
      display: flex;
      gap: 12px;
    }

    .success-message {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 16px;
      background: var(--uui-color-positive-emphasis);
      border: 2px solid var(--uui-color-positive);
      border-radius: var(--uui-border-radius);
      margin-top: 16px;
      color: #fff;
    }

    .success-message uui-icon {
      font-size: 1.5rem;
      flex-shrink: 0;
      color: #fff;
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

customElements.define(
  'umediaops-unused-media-finder-dashboard',
  UnusedMediaFinderDashboard,
)
