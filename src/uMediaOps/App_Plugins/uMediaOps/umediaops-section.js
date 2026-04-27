import { LitElement, html } from '@umbraco-cms/backoffice/external/lit'

export default class uMediaOpsSection extends LitElement {
  render() {
    return html`
      <umb-body-layout headline="uMediaOps">
        <div id="main">
          <umb-section-main-views></umb-section-main-views>
        </div>
      </umb-body-layout>
    `
  }
}

customElements.define('umediaops-section', uMediaOpsSection)
