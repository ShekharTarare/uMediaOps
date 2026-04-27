import { LitElement, html, css } from '@umbraco-cms/backoffice/external/lit'

/**
 * uMediaOps Loading Spinner Component
 * Reusable loading indicator with uMediaOps branding
 */
class LoadingSpinner extends LitElement {
  static properties = {
    size: { type: String },
    message: { type: String },
  }

  constructor() {
    super()
    this.size = 'medium' // small, medium, large
    this.message = 'Loading...'
  }

  static styles = css`
    :host {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-6);
    }

    .spinner {
      border-radius: 50%;
      border: 3px solid rgba(0, 181, 163, 0.1);
      border-top-color: var(--umediaops-primary-color, #00B5A3);
      animation: spin 1s linear infinite;
    }

    .spinner.small {
      width: 24px;
      height: 24px;
      border-width: 2px;
    }

    .spinner.medium {
      width: 48px;
      height: 48px;
      border-width: 3px;
    }

    .spinner.large {
      width: 72px;
      height: 72px;
      border-width: 4px;
    }

    .message {
      color: var(--uui-color-text-alt);
      font-size: 14px;
      text-align: center;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }

    /* Pulse animation for the message */
    .message {
      animation: pulse 2s ease-in-out infinite;
    }

    @keyframes pulse {
      0%,
      100% {
        opacity: 1;
      }
      50% {
        opacity: 0.5;
      }
    }
  `

  render() {
    return html`
      <div class="spinner ${this.size}"></div>
      ${this.message ? html`<div class="message">${this.message}</div>` : ''}
    `
  }
}

customElements.define('loading-spinner', LoadingSpinner)
export { LoadingSpinner }
