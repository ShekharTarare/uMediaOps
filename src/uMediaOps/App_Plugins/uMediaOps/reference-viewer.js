import { LitElement, css, html } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'
import { AuthenticationHelper } from './authentication-helper.js'
import { NotificationHelper } from './notification-helper.js'

export class ReferenceViewer extends UmbElementMixin(LitElement) {
  static properties = {
    mediaId: { type: Number },
    references: { type: Array },
    statistics: { type: Object },
    loading: { type: Boolean },
    error: { type: String },
    expanded: { type: Boolean },
  }

  constructor() {
    super()
    this.mediaId = 0
    this.references = []
    this.statistics = null
    this.loading = false
    this.error = null
    this.expanded = false
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
    } catch (error) {
      this.handleAuthError(error)
    }
  }

  // Watch for mediaId changes and load references immediately
  updated(changedProperties) {
    if (changedProperties.has('mediaId') && this.mediaId) {
      this.loadReferences()
    }
  }

  async loadReferences() {
    if (!this.mediaId) return

    this.loading = true
    this.error = null

    try {
      const response = await this.makeAuthenticatedRequest(
        `/umbraco/management/api/v1/umediaops/references/${this.mediaId}`,
      )

      if (!response.ok) {
        throw new Error('Failed to load references')
      }

      const data = await response.json()
      this.references = data.references || []
      this.statistics = data.statistics || {}
    } catch (err) {
      this.error = err.message
    } finally {
      this.loading = false
    }
  }

  toggleExpanded() {
    this.expanded = !this.expanded
  }

  getContentTypeIcon(contentType) {
    // Map content types to icons
    const iconMap = {
      page: 'icon-document',
      article: 'icon-article',
      blogPost: 'icon-edit',
      product: 'icon-shopping-basket',
    }
    return iconMap[contentType] || 'icon-document'
  }

  render() {
    return html`
      <div class="reference-viewer">
        <div class="reference-header" @click=${this.toggleExpanded}>
          <div class="reference-summary">
            <uui-icon
              name="${this.expanded
                ? 'icon-navigation-down'
                : 'icon-navigation-right'}"
            ></uui-icon>
            <span class="reference-title">
              ${this.statistics?.totalReferences || 0}
              Reference${this.statistics?.totalReferences !== 1 ? 's' : ''}
            </span>
            ${this.statistics?.isSafeToDelete === false
              ? html`
                  <span class="warning-badge">
                    <uui-icon name="icon-alert"></uui-icon>
                    In Use
                  </span>
                `
              : html`
                  <span class="safe-badge">
                    <uui-icon name="icon-check"></uui-icon>
                    Safe to Delete
                  </span>
                `}
          </div>
        </div>

        ${this.expanded
          ? html`
              <div class="reference-content">
                ${this.loading
                  ? html`
                      <div class="loading">
                        <uui-loader></uui-loader>
                        <span>Loading references...</span>
                      </div>
                    `
                  : ''}
                ${this.error
                  ? html`
                      <div class="error">
                        <uui-icon name="icon-alert"></uui-icon>
                        <span>${this.error}</span>
                      </div>
                    `
                  : ''}
                ${!this.loading && !this.error && this.references.length === 0
                  ? html`
                      <div class="no-references">
                        <uui-icon name="icon-check"></uui-icon>
                        <p>This file is not referenced in any content.</p>
                        <p class="hint">It is safe to delete.</p>
                      </div>
                    `
                  : ''}
                ${!this.loading && !this.error && this.references.length > 0
                  ? html`
                      <div class="references-list">
                        <div class="breakdown">
                          <strong>References by type:</strong>
                          ${Object.entries(
                            this.statistics?.byContentType || {},
                          ).map(
                            ([type, count]) => html`
                              <span class="type-chip"> ${type}: ${count} </span>
                            `,
                          )}
                        </div>

                        <div class="reference-items">
                          ${this.references.map(
                            (ref) => html`
                              <div class="reference-item">
                                <div class="reference-icon">
                                  <uui-icon
                                    name="${this.getContentTypeIcon(
                                      ref.contentType,
                                    )}"
                                  ></uui-icon>
                                </div>
                                <div class="reference-info">
                                  <div class="reference-name">
                                    ${ref.contentName}
                                  </div>
                                  <div class="reference-meta">
                                    <span class="content-type"
                                      >${ref.contentType}</span
                                    >
                                    <span class="separator">•</span>
                                    <span class="property"
                                      >Property: ${ref.propertyAlias}</span
                                    >
                                  </div>
                                </div>
                                <div class="reference-actions">
                                  ${ref.url
                                    ? html`
                                        <uui-button
                                          look="outline"
                                          label="View Content"
                                          compact
                                          href="${ref.url}"
                                          target="_blank"
                                        >
                                          <uui-icon name="icon-out"></uui-icon>
                                          View
                                        </uui-button>
                                      `
                                    : ''}
                                </div>
                              </div>
                            `,
                          )}
                        </div>
                      </div>
                    `
                  : ''}
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
    }

    .reference-viewer {
      border: 2px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      overflow: hidden;
    }

    .reference-header {
      padding: var(--uui-size-space-4);
      background: var(--uui-color-surface-alt);
      cursor: pointer;
      user-select: none;
      transition: background 0.2s;
    }

    .reference-header:hover {
      background: var(--uui-color-surface-emphasis);
    }

    .reference-summary {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
    }

    .reference-title {
      font-weight: 600;
      flex: 1;
    }

    .warning-badge {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-1);
      padding: 4px 12px;
      background: #fff3cd;
      color: #856404;
      border: 1px solid #ffeaa7;
      border-radius: 12px;
      font-size: 0.85rem;
      font-weight: 600;
    }

    .safe-badge {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-1);
      padding: 4px 12px;
      background: #d4edda;
      color: #155724;
      border: 1px solid #c3e6cb;
      border-radius: 12px;
      font-size: 0.85rem;
      font-weight: 600;
    }

    .reference-content {
      padding: var(--uui-size-space-4);
      background: var(--uui-color-surface);
    }

    .loading,
    .error,
    .no-references {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--uui-size-space-2);
      padding: var(--uui-size-space-5);
      text-align: center;
      flex-direction: column;
    }

    .error {
      color: var(--uui-color-danger);
    }

    .no-references {
      color: var(--uui-color-positive);
    }

    .no-references .hint {
      color: var(--uui-color-text-alt);
      font-size: 0.9rem;
      margin: 0;
    }

    .breakdown {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      margin-bottom: var(--uui-size-space-4);
      flex-wrap: wrap;
    }

    .type-chip {
      padding: 4px 12px;
      background: var(--uui-color-surface-alt);
      border-radius: 12px;
      font-size: 0.85rem;
    }

    .reference-items {
      display: flex;
      flex-direction: column;
      gap: var(--uui-size-space-3);
    }

    .reference-item {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-3);
      background: var(--uui-color-surface-alt);
      border-radius: var(--uui-border-radius);
      transition: transform 0.2s;
    }

    .reference-item:hover {
      transform: translateX(4px);
    }

    .reference-icon {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 40px;
      height: 40px;
      background: var(--uui-color-surface);
      border-radius: 50%;
    }

    .reference-info {
      flex: 1;
    }

    .reference-name {
      font-weight: 600;
      margin-bottom: var(--uui-size-space-1);
    }

    .reference-meta {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      font-size: 0.85rem;
      color: var(--uui-color-text-alt);
    }

    .separator {
      color: var(--uui-color-border);
    }

    .reference-actions {
      display: flex;
      gap: var(--uui-size-space-2);
    }
  `
}

customElements.define('umediaops-reference-viewer', ReferenceViewer)
