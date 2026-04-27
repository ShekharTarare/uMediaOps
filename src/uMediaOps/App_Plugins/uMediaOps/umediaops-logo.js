import { LitElement, html, css } from '@umbraco-cms/backoffice/external/lit'
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api'

/**
 * uMediaOps Logo Component
 * variant="dark" — brand colors for light backgrounds
 * variant="light" — white hexagon/pipe/text for dark/colored backgrounds
 */
export class uMediaOpsLogo extends UmbElementMixin(LitElement) {
  static properties = {
    size: { type: String },
    showText: { type: Boolean },
    variant: { type: String },
  }

  constructor() {
    super()
    this.size = 'medium'
    this.showText = true
    this.variant = 'dark'
  }

  static styles = css`
    :host {
      display: inline-flex;
      align-items: center;
    }
    .logo-container {
      display: flex;
      align-items: center;
    }
    .logo-icon {
      flex-shrink: 0;
    }
    .logo-icon.small {
      width: 24px;
      height: 24px;
    }
    .logo-icon.medium {
      width: 40px;
      height: 40px;
    }
    .logo-icon.large {
      width: 60px;
      height: 60px;
    }
    .logo-with-text.small {
      height: 24px;
      width: auto;
    }
    .logo-with-text.medium {
      height: 40px;
      width: auto;
    }
    .logo-with-text.large {
      height: 60px;
      width: auto;
    }
  `

  render() {
    const isLight = this.variant === 'light'
    const hexStroke = '#00B5A3'
    const pipeStroke = isLight ? '#FFFFFF' : '#1E293B'
    const uFill = isLight ? '#FFFFFF' : '#1E293B'
    const mediaFill = '#00B5A3'
    const opsFill = isLight ? '#FFFFFF' : '#1E293B'

    if (this.showText) {
      return html`
        <div class="logo-container">
          <svg
            class="logo-with-text ${this.size}"
            viewBox="0 0 280 60"
            xmlns="http://www.w3.org/2000/svg"
          >
            <g transform="translate(15, 5)">
              <polygon
                points="25 0, 45 11, 45 39, 25 50, 5 39, 5 11"
                fill="none"
                stroke="${hexStroke}"
                stroke-width="4"
                stroke-linejoin="round"
              />
              <polygon
                points="19 16, 19 34, 35 25"
                fill="#F05023"
                stroke="#F05023"
                stroke-width="2"
                stroke-linejoin="round"
              />
              <path
                d="M -8 13 L 10 13 Q 15 13 15 18 L 15 42 Q 15 47 20 47 L 27 47"
                fill="none"
                stroke="${pipeStroke}"
                stroke-width="4"
                stroke-linecap="round"
              />
            </g>
            <text
              x="75"
              y="42"
              font-family="'Segoe UI', system-ui, sans-serif"
              font-size="34"
              font-weight="800"
              letter-spacing="-0.5"
            >
              <tspan fill="${uFill}">u</tspan>
              <tspan fill="${mediaFill}">Media</tspan>
              <tspan fill="${opsFill}">Ops</tspan>
            </text>
          </svg>
        </div>
      `
    }

    return html`
      <div class="logo-container">
        <svg
          class="logo-icon ${this.size}"
          viewBox="0 0 60 60"
          xmlns="http://www.w3.org/2000/svg"
        >
          <g transform="translate(5, 5)">
            <polygon
              points="25 0, 45 11, 45 39, 25 50, 5 39, 5 11"
              fill="none"
              stroke="${hexStroke}"
              stroke-width="4"
              stroke-linejoin="round"
            />
            <polygon
              points="19 16, 19 34, 35 25"
              fill="#F05023"
              stroke="#F05023"
              stroke-width="2"
              stroke-linejoin="round"
            />
            <path
              d="M -8 13 L 10 13 Q 15 13 15 18 L 15 42 Q 15 47 20 47 L 27 47"
              fill="none"
              stroke="${pipeStroke}"
              stroke-width="4"
              stroke-linecap="round"
            />
          </g>
        </svg>
      </div>
    `
  }
}

customElements.define('umediaops-logo', uMediaOpsLogo)
