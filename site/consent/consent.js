// Shows the result Microsoft Entra appends when it sends the browser here after the
// admin-consent link (docs/app-registration-setup.md): admin_consent=True&tenant=… on
// success, error=…&error_description=… otherwise.
//
// Anyone can build this URL with parameters of their own, so the page shows only its own
// text plus values that match a strict pattern — a GUID or an AADSTS number. Free text from
// the URL would let a crafted link put someone else's words on a junis page.
(function () {
  'use strict';

  var guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  var params = new URLSearchParams(window.location.search);

  function show(id) {
    document.getElementById(id).hidden = false;
  }

  function fill(id, value) {
    document.getElementById(id).textContent = value;
  }

  // Before admin_consent: Entra sends admin_consent=True with a failure too
  // (?admin_consent=True&error=consent_required&error_description=AADSTS65004…).
  if (params.has('error')) {
    var description = params.get('error_description') || '';
    var code = (description.match(/AADSTS\d{4,8}/) || [])[0];

    // AADSTS65004: the admin selected Cancel, or a non-admin sent an approval request.
    if (code === 'AADSTS65004') {
      show('cancelled');
    } else {
      if (code) {
        fill('code', code);
        show('code-line');
      }

      show('refused');
    }

    show('failed');
    return;
  }

  if ((params.get('admin_consent') || '').toLowerCase() === 'true') {
    var tenant = params.get('tenant') || '';
    if (guid.test(tenant)) {
      fill('tenant', tenant);
      show('tenant-line');
    }

    show('granted');
    return;
  }

  show('none');
}());
