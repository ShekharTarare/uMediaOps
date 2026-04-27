import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'
import './umediaops-logo.js'
import { NotificationHelper } from './notification-helper.js'
import { AuthenticationHelper } from './authentication-helper.js'

export class DuplicateGroupsList extends UmbElementMixin(LitElement) {
  static properties = {
    groups: { type: Array, state: true },
    filteredGroups: { type: Array, state: true },
    loading: { type: Boolean },
    error: { type: String },
    fileTypeFilter: { type: String },
    expandedGroups: { type: Set },
    searchQuery: { type: String },
    selectedGroups: { type: Set },
    showBulkActions: { type: Boolean },
    currentPage: { type: Number, state: true },
    pageSize: { type: Number },
    totalItems: { type: Number },
    totalDuplicateFiles: { type: Number },
    totalStorageWasted: { type: Number },
    totalPages: { type: Number },
    showDeleteConfirm: { type: Boolean, state: true },
    showStats: { type: Boolean, state: true },
  }

  constructor() {
    super()
    this.groups = []
    this.filteredGroups = []
    this.loading = false
    this.error = null
    this.fileTypeFilter = ''
    this.expandedGroups = new Set()
    this.searchQuery = ''
    this.selectedGroups = new Set()
    this.showBulkActions = false
    this.currentPage = 1
    this.pageSize = 50
    this.totalItems = 0
    this.totalDuplicateFiles = 0
    this.totalStorageWasted = 0
    this.totalPages = 1
    this.showDeleteConfirm = false
    this.searchTimeout = null
    // Load stats visibility from sessionStorage, default based on screen size
    const savedState = sessionStorage.getItem('umediaops-stats-visible')
    if (savedState !== null) {
      this.showStats = savedState === 'true'
    } else {
      this.showStats = window.innerWidth >= 768 // Default: expanded on desktop, collapsed on mobile
    }
    this.authHelper = new AuthenticationHelper(this)
  }

  async connectedCallback() {
    super.connectedCallback()

    try {
      await this.authHelper.initialize()
      await this.loadGroups(true)
    } catch (error) {
      this.handleAuthError(error)
    }
  }

  disconnectedCallback() {
    super.disconnectedCallback()
    this.authHelper.destroy()
  }

  handleAuthError(error) {
    this.error =
      'Authentication failed. Please ensure you are logged into the Umbraco backoffice.'
    NotificationHelper.showError(
      this,
      'Authentication failed. Please ensure you are logged into the Umbraco backoffice.',
    )
  }

  toggleStats() {
    this.showStats = !this.showStats
    sessionStorage.setItem('umediaops-stats-visible', this.showStats.toString())
  }

  async loadGroups(isInitialLoad = false) {
    // Only show full loading spinner on initial load, not on filter/search/pagination
    if (isInitialLoad || this.groups.length === 0) {
      this.loading = true
    }
    this.error = null

    try {
      const url = new URL(
        '/umbraco/management/api/v1/umediaops/duplicates',
        window.location.origin,
      )

      // Add file type filter
      if (this.fileTypeFilter) {
        url.searchParams.append('fileTypeFilter', this.fileTypeFilter)
      }

      // Add search filter
      if (this.searchQuery) {
        url.searchParams.append('search', this.searchQuery)
      }

      // Add pagination
      url.searchParams.append('pageNumber', this.currentPage)
      url.searchParams.append('pageSize', this.pageSize)

      const response = await this.authHelper.makeAuthenticatedRequest(url)
      if (!response.ok) {
        if (response.status === 401) {
          this.handleAuthError(new Error('Unauthorized'))
          return
        }
        throw new Error('Failed to load duplicate groups')
      }

      const data = await response.json()

      // Backend now returns paginated response
      this.groups = data.items || []
      this.filteredGroups = data.items || []
      this.totalItems = data.totalItems || 0
      this.totalDuplicateFiles = data.totalDuplicateFiles || 0
      this.totalStorageWasted = data.totalStorageWasted || 0
      this.totalPages = data.totalPages || 1
    } catch (err) {
      if (err.message.includes('Authentication')) {
        this.handleAuthError(err)
      } else {
        this.error = err.message
      }
    } finally {
      this.loading = false
    }
  }

  // Server-side pagination - no client-side filtering needed
  get paginatedGroups() {
    return this.filteredGroups || []
  }

  get totalPagesComputed() {
    return this.totalPages || 1
  }

  async goToPage(page) {
    this.currentPage = page
    await this.loadGroups()
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  async nextPage() {
    if (this.currentPage < this.totalPagesComputed) {
      await this.goToPage(this.currentPage + 1)
    }
  }

  async previousPage() {
    if (this.currentPage > 1) {
      await this.goToPage(this.currentPage - 1)
    }
  }

  async handleFilterChange(e) {
    this.fileTypeFilter = e.target.value
    this.currentPage = 1
    await this.loadGroups()
  }

  async handleSearchInput(e) {
    this.searchQuery = e.target.value
    // Debounce search - wait for user to stop typing
    clearTimeout(this.searchTimeout)
    this.searchTimeout = setTimeout(async () => {
      this.currentPage = 1
      await this.loadGroups()
    }, 500)
  }

  viewGroupDetail(hash) {
    this.dispatchEvent(
      new CustomEvent('view-detail', {
        detail: { hash },
        bubbles: true,
        composed: true,
      }),
    )
  }

  toggleExpand(hash) {
    if (this.expandedGroups.has(hash)) {
      this.expandedGroups.delete(hash)
    } else {
      this.expandedGroups.add(hash)
    }
    this.expandedGroups = new Set(this.expandedGroups)
    this.requestUpdate()
  }

  toggleGroupSelection(hash) {
    if (this.selectedGroups.has(hash)) {
      this.selectedGroups.delete(hash)
    } else {
      this.selectedGroups.add(hash)
    }
    this.selectedGroups = new Set(this.selectedGroups)
    this.showBulkActions = this.selectedGroups.size > 0
    this.requestUpdate()
  }

  selectAllGroups() {
    this.selectedGroups = new Set(this.filteredGroups.map((g) => g.hash))
    this.showBulkActions = true
    this.requestUpdate()
  }

  async selectAllGroupsAcrossPages() {
    await NotificationHelper.showInfo(
      this,
      `Selecting all ${this.totalItems} groups...`,
    )
    const allHashes = []
    for (let page = 1; page <= this.totalPagesComputed; page++) {
      try {
        const url = new URL(
          '/umbraco/management/api/v1/umediaops/duplicates',
          window.location.origin,
        )
        url.searchParams.append('pageNumber', page)
        url.searchParams.append('pageSize', this.pageSize)
        const response = await this.authHelper.makeAuthenticatedRequest(url)
        if (response.ok) {
          const data = await response.json()
          const items = data.items || []
          items.forEach((g) => allHashes.push(g.hash))
        }
      } catch (err) {
        /* continue */
      }
    }
    this.selectedGroups = new Set(allHashes)
    this.showBulkActions = true
    this.requestUpdate()
    await NotificationHelper.showSuccess(
      this,
      `Selected all ${allHashes.length} groups`,
    )
  }

  clearSelection() {
    this.selectedGroups.clear()
    this.showBulkActions = false
    this.requestUpdate()
  }

  openDeleteConfirm() {
    this.showDeleteConfirm = true
  }

  closeDeleteConfirm() {
    this.showDeleteConfirm = false
  }

  async deleteAllDuplicatesInSelectedGroups() {
    this.showDeleteConfirm = false

    const mediaIdsToDelete = []
    this.filteredGroups.forEach((group) => {
      if (this.selectedGroups.has(group.hash)) {
        // Add all non-original files
        group.items.forEach((item) => {
          if (!item.isOriginal) {
            mediaIdsToDelete.push(item.mediaId)
          }
        })
      }
    })

    try {
      const response = await this.authHelper.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/duplicates/delete',
        {
          method: 'POST',
          body: JSON.stringify({ mediaIds: mediaIdsToDelete }),
        },
      )

      if (!response.ok) {
        if (response.status === 401) {
          this.handleAuthError(new Error('Unauthorized'))
          return
        }
        throw new Error('Failed to delete duplicates')
      }

      const result = await response.json()

      // Clear scan cache
      await this.authHelper.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/scan/clear',
        {
          method: 'POST',
        },
      )

      // Show success notification
      await NotificationHelper.showSuccess(
        this,
        `Successfully deleted ${result.deletedCount} files, freed ${this.formatBytes(result.spaceFreed)}`,
      )

      // Reload groups
      this.clearSelection()
      await this.loadGroups()
    } catch (err) {
      if (err.message.includes('Authentication')) {
        this.handleAuthError(err)
      } else {
        // Show error notification
        await NotificationHelper.showError(this, `Error: ${err.message}`)
      }
    }
  }

  async exportToCSV() {
    try {
      await NotificationHelper.showInfo(this, 'Preparing CSV export...')

      // Load ALL groups across all pages
      const allGroups = []
      for (let page = 1; page <= this.totalPagesComputed; page++) {
        const url = new URL(
          '/umbraco/management/api/v1/umediaops/duplicates',
          window.location.origin,
        )
        url.searchParams.append('pageNumber', page)
        url.searchParams.append('pageSize', this.pageSize)
        const response = await this.authHelper.makeAuthenticatedRequest(url)
        if (response.ok) {
          const data = await response.json()
          allGroups.push(...(data.items || []))
        }
      }

      const rows = [
        [
          'Group Hash',
          'File Name',
          'File Type',
          'Size (Bytes)',
          'Size',
          'Upload Date',
          'Is Original',
          'Media ID',
          'Uploader',
        ].join(','),
      ]

      allGroups.forEach((group) => {
        group.items.forEach((item) => {
          rows.push(
            [
              this.escapeCsv(group.hash),
              this.escapeCsv(item.name),
              this.escapeCsv(item.extension || item.fileType),
              item.fileSize,
              this.escapeCsv(this.formatBytes(item.fileSize)),
              this.escapeCsv(new Date(item.uploadDate).toLocaleString()),
              item.isOriginal ? 'Yes' : 'No',
              item.mediaId,
              this.escapeCsv(item.uploaderName || 'Unknown'),
            ].join(','),
          )
        })
      })

      const csv = rows.join('\n')
      const blob = new Blob(['\ufeff' + csv], {
        type: 'text/csv;charset=utf-8',
      })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `duplicates-${new Date().toISOString().split('T')[0]}.csv`
      a.click()
      URL.revokeObjectURL(url)

      await NotificationHelper.showSuccess(
        this,
        `Exported ${allGroups.length} groups to CSV`,
      )
    } catch (err) {
      await NotificationHelper.showError(this, `Export failed: ${err.message}`)
    }
  }

  escapeCsv(value) {
    if (value == null) return ''
    const str = String(value)
    // Prevent CSV injection - prefix formula-triggering characters
    let safe = str
    if (safe.length > 0 && '=+-@\t\r'.includes(safe[0])) {
      safe = "'" + safe
    }
    if (safe.includes(',') || safe.includes('"') || safe.includes('\n')) {
      return '"' + safe.replace(/"/g, '""') + '"'
    }
    return safe
  }

  isImage(item) {
    const ext = item.extension?.toLowerCase() || ''
    const type = item.fileType?.toLowerCase() || ''
    return (
      type.includes('image') ||
      ['jpg', 'jpeg', 'png', 'gif', 'webp', 'svg'].includes(ext)
    )
  }

  getFileIcon(item) {
    const ext = item.extension?.toLowerCase() || ''
    const type = item.fileType?.toLowerCase() || ''

    if (
      type.includes('image') ||
      ['jpg', 'jpeg', 'png', 'gif', 'webp', 'svg'].includes(ext)
    ) {
      return 'icon-picture'
    }
    if (type.includes('video') || ['mp4', 'avi', 'mov', 'wmv'].includes(ext)) {
      return 'icon-video'
    }
    if (['pdf'].includes(ext)) {
      return 'icon-document-dashed-line'
    }
    if (['doc', 'docx', 'txt'].includes(ext)) {
      return 'icon-file-cabinet'
    }
    return 'icon-document'
  }

  formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes'
    const k = 1024
    const sizes = ['Bytes', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i]
  }

  render() {
    return html`
      <div class="groups-container">
        ${
          !this.loading && this.filteredGroups.length > 0
            ? html`
                <div class="stats-section">
                  <div class="stats-header">
                    <h3>Statistics</h3>
                    <uui-button
                      look="outline"
                      label="${this.showStats ? 'Hide' : 'Show'} Statistics"
                      @click=${this.toggleStats}
                      compact
                    >
                      <uui-icon
                        name="icon-${this.showStats
                          ? 'navigation-up'
                          : 'navigation-down'}"
                      ></uui-icon>
                      ${this.showStats ? 'Hide' : 'Show'}
                    </uui-button>
                  </div>
                  ${this.showStats
                    ? html`
                        <div class="stats-bar">
                          <div class="stat-card">
                            <div class="stat-label">Total Groups</div>
                            <div class="stat-value">${this.totalItems}</div>
                          </div>
                          <div class="stat-card">
                            <div class="stat-label">Duplicate Files</div>
                            <div class="stat-value">
                              ${this.totalDuplicateFiles}
                            </div>
                          </div>
                          <div class="stat-card highlight">
                            <div class="stat-label">Storage Wasted</div>
                            <div class="stat-value">
                              ${this.formatBytes(this.totalStorageWasted)}
                            </div>
                          </div>
                        </div>
                      `
                    : ''}
                </div>
              `
            : ''
        }
        ${
          this.error
            ? html`
                <uui-box>
                  <p style="color: var(--uui-color-danger);">${this.error}</p>
                </uui-box>
              `
            : ''
        }

        <div class="controls-section">
          <div class="primary-controls">
            <div class="control-group search-box">
              <uui-icon name="icon-search"></uui-icon>
              <input
                type="text"
                role="searchbox"
                aria-label="Search duplicate groups by filename"
                placeholder="Search by filename..."
                .value=${this.searchQuery}
                @input=${this.handleSearchInput}
              />
            </div>

            <div class="control-group filter-group">
              <label>Filter by type:</label>
              <select
                @change=${this.handleFilterChange}
                .value=${this.fileTypeFilter}
              >
                <option value="">All Types</option>
                <option value="image">Images</option>
                <option value="file">Files</option>
                <option value="video">Videos</option>
              </select>
            </div>

            <uui-button
              look="primary"
              label="Refresh"
              @click=${this.loadGroups}
            >
              <uui-icon name="icon-refresh"></uui-icon>
              Refresh
            </uui-button>
          </div>

          </div>
        </div>

        ${
          this.showBulkActions
            ? html`
                <div class="bulk-actions">
                  <span
                    >${this.selectedGroups.size}
                    group${this.selectedGroups.size > 1 ? 's' : ''}
                    selected</span
                  >
                  <uui-button
                    look="primary"
                    color="danger"
                    label="Delete All Duplicates"
                    @click=${this.openDeleteConfirm}
                  >
                    <uui-icon name="icon-trash"></uui-icon>
                    Delete All Duplicates
                  </uui-button>
                  <uui-button
                    look="outline"
                    label="Clear Selection"
                    @click=${this.clearSelection}
                  >
                    Clear
                  </uui-button>
                </div>
              `
            : ''
        }
        ${
          this.loading
            ? html`
                <div class="loading">
                  <uui-loader></uui-loader>
                  <p>Loading duplicate groups...</p>
                </div>
              `
            : ''
        }
        ${
          !this.loading && this.filteredGroups.length === 0
            ? html`
                <uui-box>
                  <div class="empty-state">
                    <uui-icon
                      name="icon-check"
                      style="font-size: 3rem; color: var(--uui-color-positive);"
                    ></uui-icon>
                    <h2>No Duplicates Found</h2>
                    <p>
                      ${this.searchQuery
                        ? 'No duplicates match your search.'
                        : 'Your media library is clean! No duplicate files were detected.'}
                    </p>
                  </div>
                </uui-box>
              `
            : ''
        }
        ${
          !this.loading && this.filteredGroups.length > 0
            ? html`
                <div class="summary">
                  <strong>${this.totalItems}</strong> duplicate
                  group${this.totalItems !== 1 ? 's' : ''} found
                  <span style="margin-left: auto;">
                    Showing
                    ${(this.currentPage - 1) * this.pageSize + 1}-${Math.min(
                      this.currentPage * this.pageSize,
                      this.totalItems,
                    )}
                    of ${this.totalItems}
                  </span>
                  <uui-button
                    look="outline"
                    label="Export CSV"
                    @click=${this.exportToCSV}
                    compact
                  >
                    <uui-icon name="icon-download"></uui-icon>
                    Export
                  </uui-button>
                  <uui-button
                    look="outline"
                    label="Select Page"
                    @click=${this.selectAllGroups}
                    compact
                  >
                    Select Page
                  </uui-button>
                  <uui-button
                    look="primary"
                    color="positive"
                    label="Select All"
                    @click=${this.selectAllGroupsAcrossPages}
                    compact
                    ?disabled=${this.totalItems === 0}
                  >
                    <uui-icon name="icon-check"></uui-icon>
                    Select All (${this.totalItems})
                  </uui-button>
                </div>

                <div
                  class="groups-grid"
                  role="list"
                  aria-label="Duplicate file groups"
                >
                  ${this.paginatedGroups.map(
                    (group) => html`
                      <uui-box class="group-card" role="listitem">
                        <div class="group-header">
                          <input
                            type="checkbox"
                            role="checkbox"
                            aria-checked="${this.selectedGroups.has(
                              group.hash,
                            )}"
                            aria-label="Select duplicate group with ${group.count} files"
                            .checked=${this.selectedGroups.has(group.hash)}
                            @change=${() =>
                              this.toggleGroupSelection(group.hash)}
                          />
                          <div class="group-count">
                            <uui-icon name="icon-files"></uui-icon>
                            ${group.count} files
                          </div>
                          <div class="group-size">
                            ${this.formatBytes(group.totalSize)}
                          </div>
                        </div>

                        <div class="group-details">
                          <div class="detail-item">
                            <span class="label">Duplicates:</span>
                            <span class="value">${group.count - 1}</span>
                          </div>
                          <div class="detail-item">
                            <span class="label">Wasted Space:</span>
                            <span class="value"
                              >${this.formatBytes(
                                group.totalSize - group.totalSize / group.count,
                              )}</span
                            >
                          </div>
                        </div>

                        <div class="group-files">
                          ${group.items.slice(0, 3).map(
                            (item) => html`
                              <div class="file-preview">
                                ${this.isImage(item) && item.fileUrl
                                  ? html`
                                      <img
                                        src="${item.fileUrl}?width=60&height=60&mode=crop"
                                        alt="${item.name}"
                                        class="file-thumbnail"
                                        @error=${(e) => {
                                          e.target.style.display = 'none'
                                          e.target.nextElementSibling.style.display =
                                            'inline-block'
                                        }}
                                      />
                                      <uui-icon
                                        name="${this.getFileIcon(item)}"
                                        style="display: none;"
                                      ></uui-icon>
                                    `
                                  : html`
                                      <uui-icon
                                        name="${this.getFileIcon(item)}"
                                      ></uui-icon>
                                    `}
                                <span class="file-name">${item.name}</span>
                              </div>
                            `,
                          )}
                          ${this.expandedGroups.has(group.hash)
                            ? group.items.slice(3).map(
                                (item) => html`
                                  <div class="file-preview">
                                    ${this.isImage(item) && item.fileUrl
                                      ? html`
                                          <img
                                            src="${item.fileUrl}?width=40&height=40&mode=crop"
                                            alt="${item.name}"
                                            class="file-thumbnail"
                                            @error=${(e) => {
                                              e.target.style.display = 'none'
                                              e.target.nextElementSibling.style.display =
                                                'inline-block'
                                            }}
                                          />
                                          <uui-icon
                                            name="${this.getFileIcon(item)}"
                                            style="display: none;"
                                          ></uui-icon>
                                        `
                                      : html`
                                          <uui-icon
                                            name="${this.getFileIcon(item)}"
                                          ></uui-icon>
                                        `}
                                    <span class="file-name">${item.name}</span>
                                  </div>
                                `,
                              )
                            : ''}
                          ${group.items.length > 3
                            ? html`
                                <div
                                  class="more-files"
                                  @click=${(e) => {
                                    e.stopPropagation()
                                    this.toggleExpand(group.hash)
                                  }}
                                  style="cursor: pointer; color: var(--uui-color-interactive);"
                                >
                                  ${this.expandedGroups.has(group.hash)
                                    ? 'Show less'
                                    : `+${group.items.length - 3} more`}
                                </div>
                              `
                            : ''}
                        </div>

                        <uui-button
                          look="primary"
                          label="View Details"
                          compact
                          @click=${() => this.viewGroupDetail(group.hash)}
                        >
                          View Details
                        </uui-button>
                      </uui-box>
                    `,
                  )}
                </div>

                ${this.totalPagesComputed > 1
                  ? html`
                      <div class="pagination">
                        <uui-button
                          look="outline"
                          label="Previous"
                          @click=${this.previousPage}
                          ?disabled=${this.currentPage === 1}
                          compact
                        >
                          <uui-icon name="icon-arrow-left"></uui-icon>
                          Previous
                        </uui-button>

                        <div class="page-numbers">
                          ${Array.from(
                            { length: this.totalPagesComputed },
                            (_, i) => i + 1,
                          )
                            .filter((page) => {
                              // Show first page, last page, current page, and pages around current
                              return (
                                page === 1 ||
                                page === this.totalPagesComputed ||
                                Math.abs(page - this.currentPage) <= 2
                              )
                            })
                            .map((page, index, array) => {
                              // Add ellipsis if there's a gap
                              const prevPage = array[index - 1]
                              const showEllipsis =
                                prevPage && page - prevPage > 1
                              return html`
                                ${showEllipsis
                                  ? html`<span class="ellipsis">...</span>`
                                  : ''}
                                <uui-button
                                  look=${page === this.currentPage
                                    ? 'primary'
                                    : 'outline'}
                                  label="Page ${page}"
                                  @click=${() => this.goToPage(page)}
                                  compact
                                >
                                  ${page}
                                </uui-button>
                              `
                            })}
                        </div>

                        <uui-button
                          look="outline"
                          label="Next"
                          @click=${this.nextPage}
                          ?disabled=${this.currentPage ===
                          this.totalPagesComputed}
                          compact
                        >
                          Next
                          <uui-icon name="icon-arrow-right"></uui-icon>
                        </uui-button>
                      </div>
                    `
                  : ''}
              `
            : ''
        }
        ${
          this.showDeleteConfirm
            ? html`
                <div class="dialog-overlay" @click=${this.closeDeleteConfirm}>
                  <div class="dialog" @click=${(e) => e.stopPropagation()}>
                    <h2>
                      <uui-icon name="icon-alert"></uui-icon>
                      Confirm Deletion
                    </h2>
                    <p>
                      You are about to delete all duplicates in
                      <strong>${this.selectedGroups.size}</strong> selected
                      group${this.selectedGroups.size > 1 ? 's' : ''}.
                    </p>
                    <p>
                      This will keep the original files and only remove the
                      duplicates.
                    </p>
                    <p
                      style="color: var(--uui-color-danger); font-weight: 500;"
                    >
                      <uui-icon name="icon-alert"></uui-icon>
                      This action cannot be undone!
                    </p>

                    <div class="dialog-actions">
                      <uui-button
                        look="outline"
                        label="Cancel"
                        @click=${this.closeDeleteConfirm}
                      >
                        Cancel
                      </uui-button>
                      <uui-button
                        look="primary"
                        color="danger"
                        label="Delete"
                        @click=${this.deleteAllDuplicatesInSelectedGroups}
                      >
                        <uui-icon name="icon-trash"></uui-icon>
                        Delete Duplicates
                      </uui-button>
                    </div>
                  </div>
                </div>
              `
            : ''
        }
      </div>
    `
  }

  static styles = css`
    :host {
      display: block;
    }
    }

    .groups-container {
      max-width: 1400px;
      margin: 0 auto;
      width: 100%;
    }

    .header {
      margin-bottom: 12px;
    }

    .title-section {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 4px;
    }

    .header h1 {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0;
      font-size: 1.5rem;
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

    .header p {
      color: var(--uui-color-text-alt);
      margin: 0;
    }

    .controls-section {
      margin-bottom: 12px;
    }

    .primary-controls {
      display: flex;
      gap: 12px;
      align-items: center;
      margin-bottom: 12px;
      flex-wrap: wrap;
      padding: 12px;
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      border: 1px solid var(--uui-color-border);
    }

    .stats-section {
      margin-bottom: 12px;
    }

    .stats-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 8px;
    }

    .stats-header h3 {
      margin: 0;
      font-size: 0.95rem;
      font-weight: 600;
    }

    .stats-bar {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
      gap: 10px;
      animation: fadeIn 0.3s ease;
    }

    .stat-card {
      padding: 12px;
      background: var(--uui-color-surface);
      border: 2px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      text-align: center;
      transition:
        transform 0.2s,
        box-shadow 0.2s,
        border-color 0.2s;
      cursor: default;
    }

    .stat-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
      border-color: var(--uui-color-interactive);
    }

    .stat-card.highlight {
      background: linear-gradient(135deg, #00B5A315 0%, #1E293B15 100%);
      border-color: #00B5A3;
    }

    .stat-label {
      font-size: 0.7rem;
      color: var(--uui-color-text-alt);
      margin-bottom: 4px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .stat-value {
      font-size: 1.3rem;
      font-weight: bold;
      color: var(--uui-color-text);
    }

    .stat-card.highlight .stat-value {
      color: #00B5A3;
    }

    .control-group {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
    }

    .filter-group {
      background: var(--uui-color-surface);
      padding: 8px 12px;
      border-radius: var(--uui-border-radius);
      border: 1px solid var(--uui-color-border);
    }

    .search-box {
      flex: 1;
      min-width: 300px;
      position: relative;
    }

    .search-box input {
      width: 100%;
      padding: var(--uui-size-space-2) var(--uui-size-space-3);
      padding-left: var(--uui-size-space-6);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface);
    }

    .search-box uui-icon {
      position: absolute;
      left: var(--uui-size-space-2);
      top: 50%;
      transform: translateY(-50%);
      color: var(--uui-color-text-alt);
    }

    .control-group label {
      font-weight: 500;
    }

    .control-group select {
      padding: var(--uui-size-space-2) var(--uui-size-space-3);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface);
      color: var(--uui-color-text);
    }

    .control-group select option {
      background: var(--uui-color-surface);
      color: var(--uui-color-text);
    }

    .control-group select option:checked {
      background: #0066cc;
      color: white;
    }

    .bulk-actions {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      background: linear-gradient(135deg, #00B5A315 0%, #1E293B15 100%);
      border: 2px solid #00B5A3;
      border-radius: var(--uui-border-radius);
      margin-bottom: 12px;
    }

    .bulk-actions span {
      flex: 1;
      font-weight: 500;
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
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 12px;
      padding: 10px 12px;
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
    }

    .summary strong {
      flex: 1;
    }

    .groups-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(380px, 1fr));
      gap: 12px;
      margin-bottom: 12px;
    }

    .group-card {
      transition:
        transform 0.2s,
        box-shadow 0.2s,
        border-color 0.2s;
      border: 1px solid var(--uui-color-border);
    }

    .group-card:hover {
      transform: translateY(-4px);
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
      border-color: var(--uui-color-interactive);
    }

    .group-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: var(--uui-size-space-3);
      padding-bottom: var(--uui-size-space-3);
      border-bottom: 1px solid var(--uui-color-border);
      gap: var(--uui-size-space-2);
    }

    .group-header input[type='checkbox'] {
      width: 20px;
      height: 20px;
      cursor: pointer;
    }

    .group-count {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      font-weight: 600;
      flex: 1;
    }

    .group-size {
      font-size: 1.2rem;
      font-weight: bold;
      color: var(--uui-color-interactive);
    }

    .group-details {
      display: flex;
      flex-direction: column;
      gap: var(--uui-size-space-2);
      margin-bottom: var(--uui-size-space-3);
    }

    .detail-item {
      display: flex;
      justify-content: space-between;
    }

    .detail-item .label {
      color: var(--uui-color-text-alt);
    }

    .detail-item .value {
      font-weight: 500;
    }

    .group-files {
      margin-bottom: var(--uui-size-space-5);
    }

    .file-preview {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-3);
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      margin-bottom: var(--uui-size-space-2);
      font-size: 0.9rem;
    }

    .file-thumbnail {
      width: 60px;
      height: 60px;
      object-fit: cover;
      border-radius: 4px;
      flex-shrink: 0;
    }

    .file-name {
      flex: 1;
      min-width: 0;
      overflow-wrap: break-word;
      word-wrap: break-word;
      hyphens: auto;
    }

    .file-ext {
      color: var(--uui-color-text-alt);
      font-size: 0.85rem;
    }

    .more-files {
      padding: var(--uui-size-space-2);
      text-align: center;
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
    }

    .pagination {
      display: flex;
      justify-content: center;
      align-items: center;
      gap: 12px;
      margin-top: 12px;
      padding: 12px;
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
    }

    .page-numbers {
      display: flex;
      gap: var(--uui-size-space-2);
      align-items: center;
    }

    .ellipsis {
      padding: 0 var(--uui-size-space-2);
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
      z-index: 10000;
    }

    .dialog {
      background: var(--uui-color-surface);
      padding: 24px; /* 24px padding in panels */
      border-radius: var(--uui-border-radius);
      max-width: 500px;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.2);
    }

    .dialog h2 {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      margin-top: 0;
      color: var(--uui-color-danger);
    }

    .dialog-actions {
      display: flex;
      gap: 12px; /* 12px spacing between buttons */
      justify-content: flex-end;
      margin-top: 16px; /* Spacing from content */
    }
  `
}

customElements.define('umediaops-duplicate-groups-list', DuplicateGroupsList)
