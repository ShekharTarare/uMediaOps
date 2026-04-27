import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'
import './reference-viewer.js'
import { NotificationHelper } from './notification-helper.js'
import { AuthenticationHelper } from './authentication-helper.js'

export class DuplicateGroupDetail extends UmbElementMixin(LitElement) {
  static properties = {
    hash: { type: String },
    group: { type: Object },
    loading: { type: Boolean },
    error: { type: String },
    selectedIds: { type: Array },
    deleting: { type: Boolean },
    showConfirmDialog: { type: Boolean },
  }

  constructor() {
    super()
    this.hash = ''
    this.group = null
    this.loading = false
    this.error = null
    this.selectedIds = []
    this.deleting = false
    this.showConfirmDialog = false
    this.authHelper = new AuthenticationHelper(this)
  }

  async connectedCallback() {
    super.connectedCallback()

    try {
      await this.authHelper.initialize()

      // If hash is provided as property, use it
      if (this.hash) {
        this.loadGroup()
        return
      }

      // Otherwise try to get from URL path
      const pathParts = window.location.pathname.split('/')

      const hashIndex = pathParts.indexOf('duplicates') + 1

      if (hashIndex > 0 && pathParts.length > hashIndex) {
        this.hash = decodeURIComponent(pathParts[hashIndex])
        this.loadGroup()
      } else {
        this.error = 'Invalid URL - no hash found'
      }
    } catch (error) {
      this.error =
        'Authentication failed. Please ensure you are logged into the Umbraco backoffice.'
      NotificationHelper.showError(this, this.error)
    }
  }

  async makeAuthenticatedRequest(url, options = {}) {
    try {
      return await this.authHelper.makeAuthenticatedRequest(url, options)
    } catch (error) {
      NotificationHelper.showError(
        this,
        'Authentication failed. Please ensure you are logged into the Umbraco backoffice.',
      )
      throw error
    }
  }

  async loadGroup() {
    if (!this.hash) {
      this.error = 'No hash provided'
      return
    }

    this.loading = true
    this.error = null

    try {
      const response = await this.makeAuthenticatedRequest(
        `/umbraco/management/api/v1/umediaops/duplicates/${encodeURIComponent(this.hash)}`,
      )

      if (!response.ok) {
        throw new Error(`Failed to load duplicate group (${response.status})`)
      }

      this.group = await response.json()
    } catch (err) {
      this.error = err.message
    } finally {
      this.loading = false
    }
  }

  toggleSelection(mediaId, isOriginal) {
    if (isOriginal) return // Cannot select original

    const index = this.selectedIds.indexOf(mediaId)
    if (index > -1) {
      this.selectedIds = this.selectedIds.filter((id) => id !== mediaId)
    } else {
      this.selectedIds = [...this.selectedIds, mediaId]
    }
  }

  selectAllDuplicates() {
    if (!this.group) return
    this.selectedIds = this.group.items
      .filter((item) => !item.isOriginal)
      .map((item) => item.mediaId)
  }

  clearSelection() {
    this.selectedIds = []
  }

  openConfirmDialog() {
    if (this.selectedIds.length === 0) return
    this.showConfirmDialog = true
  }

  closeConfirmDialog() {
    this.showConfirmDialog = false
  }

  async confirmDelete() {
    this.deleting = true
    this.error = null

    try {
      const response = await this.makeAuthenticatedRequest(
        '/umbraco/management/api/v1/umediaops/duplicates/delete',
        {
          method: 'POST',
          body: JSON.stringify({
            mediaIds: this.selectedIds,
          }),
        },
      )

      if (!response.ok) {
        throw new Error('Failed to delete duplicates')
      }

      const result = await response.json()

      // Check if any files were actually deleted
      if (result.deletedCount > 0) {
        // Show success notification
        NotificationHelper.showSuccess(
          this,
          `Successfully deleted ${result.deletedCount} duplicate file${result.deletedCount > 1 ? 's' : ''}`,
        )

        // Clear the cached scan results since data has changed
        await this.makeAuthenticatedRequest(
          '/umbraco/management/api/v1/umediaops/scan/clear',
          {
            method: 'POST',
          },
        )

        // Navigate back to groups list
        this.goBack()
      } else {
        // Show error if nothing was deleted
        this.error = result.errors?.join(', ') || 'No files were deleted'
        NotificationHelper.showError(this, this.error)
      }
    } catch (err) {
      this.error = err.message
      NotificationHelper.showError(
        this,
        `Failed to delete files: ${err.message}`,
      )
    } finally {
      this.deleting = false
      this.showConfirmDialog = false
    }
  }

  goBack() {
    // Dispatch event for parent component to handle
    this.dispatchEvent(
      new CustomEvent('go-back', {
        bubbles: true,
        composed: true,
      }),
    )
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

  calculateSpaceToFree() {
    if (!this.group) return 0
    const selectedItems = this.group.items.filter((item) =>
      this.selectedIds.includes(item.mediaId),
    )
    return selectedItems.reduce((sum, item) => sum + item.fileSize, 0)
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

  render() {
    return html`
      <div class="detail-container">
        <div class="header">
          <uui-button look="outline" label="Back" @click=${this.goBack}>
            <uui-icon name="icon-arrow-left"></uui-icon>
            Back to Groups
          </uui-button>

          <h1>
            <uui-icon name="icon-files"></uui-icon>
            Duplicate Group Details
          </h1>
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
                <p>Loading group details...</p>
              </div>
            `
          : ''}
        ${!this.loading && this.group
          ? html`
              <uui-box headline="Group Summary">
                <div class="summary-grid">
                  <div class="summary-item">
                    <div class="summary-label">Total Files</div>
                    <div class="summary-value">${this.group.count}</div>
                  </div>
                  <div class="summary-item">
                    <div class="summary-label">Duplicates</div>
                    <div class="summary-value">${this.group.count - 1}</div>
                  </div>
                  <div class="summary-item">
                    <div class="summary-label">Total Size</div>
                    <div class="summary-value">
                      ${this.formatBytes(this.group.totalSize)}
                    </div>
                  </div>
                  <div class="summary-item">
                    <div class="summary-label">Wasted Space</div>
                    <div class="summary-value">
                      ${this.formatBytes(
                        this.group.totalSize -
                          this.group.totalSize / this.group.count,
                      )}
                    </div>
                  </div>
                </div>
              </uui-box>

              <uui-box headline="Files in Group">
                <div class="actions-bar">
                  <div class="selection-info">
                    ${this.selectedIds.length > 0
                      ? html`
                          <strong>${this.selectedIds.length}</strong> files
                          selected
                          (${this.formatBytes(this.calculateSpaceToFree())} to
                          free)
                        `
                      : html` No files selected `}
                  </div>

                  <div class="action-buttons">
                    <uui-button
                      look="outline"
                      label="Select All Duplicates"
                      @click=${this.selectAllDuplicates}
                      ?disabled=${this.selectedIds.length ===
                      this.group.count - 1}
                    >
                      Select All Duplicates
                    </uui-button>

                    <uui-button
                      look="outline"
                      label="Clear Selection"
                      @click=${this.clearSelection}
                      ?disabled=${this.selectedIds.length === 0}
                    >
                      Clear
                    </uui-button>

                    <uui-button
                      look="primary"
                      color="danger"
                      label="Delete Selected"
                      @click=${this.openConfirmDialog}
                      ?disabled=${this.selectedIds.length === 0}
                    >
                      <uui-icon name="icon-trash"></uui-icon>
                      Delete Selected
                    </uui-button>
                  </div>
                </div>

                <!-- Reference Viewer for each file -->
                <div class="reference-section">
                  <h3>Content References</h3>
                  <p class="hint">
                    Check which content is using these files before deleting.
                  </p>
                  ${this.group.items.map(
                    (item) => html`
                      <div class="file-reference-container">
                        <div class="file-reference-header">
                          <strong>${item.name}</strong>
                          ${item.isOriginal
                            ? html`<span class="badge original-badge"
                                >ORIGINAL</span
                              >`
                            : ''}
                        </div>
                        <umediaops-reference-viewer
                          .mediaId=${item.mediaId}
                        ></umediaops-reference-viewer>
                      </div>
                    `,
                  )}
                </div>

                <div class="files-list">
                  ${this.group.items.map(
                    (item) => html`
                      <div
                        class="file-item ${item.isOriginal
                          ? 'original'
                          : ''} ${this.selectedIds.includes(item.mediaId)
                          ? 'selected'
                          : ''}"
                      >
                        <div class="file-checkbox">
                          ${!item.isOriginal
                            ? html`
                                <input
                                  type="checkbox"
                                  .checked=${this.selectedIds.includes(
                                    item.mediaId,
                                  )}
                                  @change=${() =>
                                    this.toggleSelection(
                                      item.mediaId,
                                      item.isOriginal,
                                    )}
                                />
                              `
                            : html`
                                <uui-icon
                                  name="icon-lock"
                                  style="color: var(--uui-color-positive);"
                                ></uui-icon>
                              `}
                        </div>

                        <div class="file-info">
                          <div class="file-name">
                            ${this.isImage(item) && item.fileUrl
                              ? html`
                                  <img
                                    src="${item.fileUrl}?width=80&height=80&mode=crop"
                                    alt="${item.name}"
                                    class="file-thumbnail-large"
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
                            <div class="file-name-text">
                              <strong>${item.name}</strong>
                              ${item.extension
                                ? html`<span class="file-ext"
                                    >.${item.extension}</span
                                  >`
                                : ''}
                              ${item.isOriginal
                                ? html`
                                    <span class="badge original-badge"
                                      >ORIGINAL</span
                                    >
                                  `
                                : html`
                                    <span class="badge duplicate-badge"
                                      >DUPLICATE</span
                                    >
                                  `}
                            </div>
                          </div>
                          <div class="file-meta">
                            <span><strong>Path:</strong> ${item.path}</span>
                            <span
                              ><strong>Size:</strong> ${this.formatBytes(
                                item.fileSize,
                              )}</span
                            >
                            <span
                              ><strong>Uploaded:</strong> ${this.formatDate(
                                item.uploadDate,
                              )}</span
                            >
                          </div>
                        </div>
                      </div>
                    `,
                  )}
                </div>
              </uui-box>
            `
          : ''}
        ${this.showConfirmDialog
          ? html`
              <div class="dialog-overlay" @click=${this.closeConfirmDialog}>
                <div class="dialog" @click=${(e) => e.stopPropagation()}>
                  <h2>Confirm Deletion</h2>
                  <p>
                    You are about to delete
                    <strong>${this.selectedIds.length}</strong> duplicate
                    file(s). This will free up
                    <strong
                      >${this.formatBytes(this.calculateSpaceToFree())}</strong
                    >
                    of storage.
                  </p>
                  <p style="color: var(--uui-color-danger); font-weight: 500;">
                    ⚠️ This action cannot be undone!
                  </p>

                  <div class="dialog-actions">
                    <uui-button
                      look="outline"
                      label="Cancel"
                      @click=${this.closeConfirmDialog}
                      ?disabled=${this.deleting}
                    >
                      Cancel
                    </uui-button>
                    <uui-button
                      look="primary"
                      color="danger"
                      label="Delete"
                      @click=${this.confirmDelete}
                      ?disabled=${this.deleting}
                    >
                      ${this.deleting ? 'Deleting...' : 'Delete Files'}
                    </uui-button>
                  </div>
                </div>
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

  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    .detail-container {
      max-width: 1400px;
      margin: 0 auto;
      width: 100%;
    }

    .header {
      margin-bottom: var(--uui-size-space-5);
    }

    .header h1 {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      margin-top: var(--uui-size-space-4);
    }

    .loading {
      text-align: center;
      padding: var(--uui-size-space-6);
    }

    uui-box {
      margin-bottom: var(--uui-size-space-5);
    }

    .summary-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: var(--uui-size-space-4);
    }

    .summary-item {
      text-align: center;
      padding: var(--uui-size-space-4);
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
    }

    .summary-label {
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
      margin-bottom: var(--uui-size-space-2);
    }

    .summary-value {
      font-size: 1.5rem;
      font-weight: bold;
      color: var(--uui-color-interactive);
    }

    .actions-bar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: var(--uui-size-space-4);
      padding: var(--uui-size-space-3);
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      flex-wrap: wrap;
      gap: var(--uui-size-space-3);
    }

    .action-buttons {
      display: flex;
      gap: var(--uui-size-space-2);
      flex-wrap: wrap;
    }

    /* Improve disabled button visibility */
    uui-button[disabled] {
      opacity: 0.5;
      cursor: not-allowed;
    }

    uui-button[color='danger'][disabled] {
      background-color: #f5f5f5 !important;
      border-color: #d0d0d0 !important;
      color: #999 !important;
    }

    .files-list {
      display: flex;
      flex-direction: column;
      gap: var(--uui-size-space-3);
    }

    .file-item {
      display: flex;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-4);
      border: 2px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      transition: all 0.2s;
    }

    .file-item.selected {
      border-color: #0066cc;
      background: #e6f2ff;
    }

    .file-item.original {
      border-color: #0d7a3f;
      background: #e6f7ed;
    }

    .file-checkbox {
      display: flex;
      align-items: center;
    }

    .file-checkbox input[type='checkbox'] {
      width: 20px;
      height: 20px;
      cursor: pointer;
    }

    .file-info {
      flex: 1;
    }

    .file-name {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      margin-bottom: var(--uui-size-space-2);
      font-size: 1.1rem;
    }

    .file-name-text {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      flex-wrap: wrap;
    }

    .file-thumbnail-large {
      width: 80px;
      height: 80px;
      object-fit: cover;
      border-radius: 4px;
      border: 1px solid var(--uui-color-border);
    }

    .file-ext {
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
    }

    .file-meta {
      display: flex;
      flex-direction: column;
      gap: var(--uui-size-space-1);
      font-size: 0.9rem;
      color: var(--uui-color-text-alt);
    }

    .file-actions {
      margin-top: var(--uui-size-space-3);
      display: flex;
      gap: var(--uui-size-space-2);
    }

    .badge {
      padding: 2px 8px;
      border-radius: 12px;
      font-size: 0.75rem;
      font-weight: 600;
    }

    .original-badge {
      background: #0d7a3f;
      color: white;
    }

    .duplicate-badge {
      background: #92400e;
      color: white;
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
      padding: var(--uui-size-space-6);
      border-radius: var(--uui-border-radius);
      max-width: 500px;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.2);
    }

    .dialog h2 {
      margin-top: 0;
    }

    .dialog-actions {
      display: flex;
      gap: var(--uui-size-space-3);
      justify-content: flex-end;
      margin-top: var(--uui-size-space-5);
    }

    .reference-section {
      margin-bottom: var(--uui-size-space-5);
      padding: var(--uui-size-space-4);
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
    }

    .reference-section h3 {
      margin-top: 0;
      margin-bottom: var(--uui-size-space-2);
    }

    .reference-section .hint {
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
      margin-bottom: var(--uui-size-space-4);
    }

    .file-reference-container {
      margin-bottom: var(--uui-size-space-3);
    }

    .file-reference-header {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      margin-bottom: var(--uui-size-space-2);
      padding: var(--uui-size-space-2);
      background: var(--uui-color-surface);
      border-radius: var(--uui-border-radius);
    }
  `
}

customElements.define('umediaops-duplicate-group-detail', DuplicateGroupDetail)
