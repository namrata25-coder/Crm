// Lightweight modal + UI helpers (no backend calls — UI only)

// Global fetch helper: sends same-origin credentials and redirects to /login on 401 Unauthorized
(function () {
  const originalFetch = window.fetch;
  window.fetch = async function (resource, init) {
    init = init || {};
    if (!init.credentials) {
      init.credentials = 'same-origin';
    }
    const response = await originalFetch(resource, init);
    if (response.status === 401) {
      const url = typeof resource === 'string' ? resource : (resource?.url || '');
      if (url.includes('/api/')) {
        window.location.href = '/login?returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
      }
    }
    return response;
  };
})();

// Safe global HTML escaping helper
window.escapeHtml = function (str) {
  if (str === null || str === undefined) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
};

function openModal(id) {
  const el = document.getElementById(id);
  if (el) {
    el.classList.add('show');
    el.classList.add('open');
  }
}

function closeModal(id) {
  const el = document.getElementById(id);
  if (el) {
    el.classList.remove('show');
    el.classList.remove('open');
  }
}

// Close modal when clicking the backdrop itself
document.addEventListener('click', function (e) {
  if (e.target.classList && e.target.classList.contains('modal-backdrop-custom')) {
    e.target.classList.remove('show');
    e.target.classList.remove('open');
  }
});

// Close modal with Escape key
document.addEventListener('keydown', function (e) {
  if (e.key === 'Escape') {
    document.querySelectorAll('.modal-backdrop-custom').forEach(m => {
      m.classList.remove('show');
      m.classList.remove('open');
    });
  }
});

// Toggle inline "add" forms within modals (e.g., Add Call, Add Follow-up)
function toggleAddForm(id) {
  const el = document.getElementById(id);
  if (!el) return;
  el.style.display = (el.style.display === 'none' || !el.style.display) ? 'block' : 'none';
}

// Simple client-side table filter used on Assigned List page
function filterAssignedTable() {
  const search = (document.getElementById('searchInput')?.value || '').toLowerCase();
  const status = document.getElementById('statusFilter')?.value || '';
  const employee = document.getElementById('employeeFilter')?.value || '';
  const date = document.getElementById('dateFilter')?.value || '';
  let visible = 0;

  document.querySelectorAll('#assignedTable tbody tr').forEach(row => {
    const text = row.innerText.toLowerCase();
    const rowStatus = row.getAttribute('data-status') || '';
    const rowEmployee = row.getAttribute('data-employee') || '';
    const rowDate = row.getAttribute('data-date') || '';

    const matchesSearch = !search || text.includes(search);
    const matchesStatus = !status || rowStatus === status;
    const matchesEmployee = !employee || rowEmployee === employee;
    const matchesDate = !date || rowDate.includes(date) || date.includes(rowDate);

    const isMatch = matchesSearch && matchesStatus && matchesEmployee && matchesDate;
    row.style.display = isMatch ? '' : 'none';
    if (isMatch) visible++;
  });
}

// Simple client-side table filter used on Lead List page
function filterLeadListTable() {
  const search = (document.getElementById('leadListSearch')?.value || '').toLowerCase();
  const status = document.getElementById('leadStatusFilter')?.value || '';
  let visible = 0;

  document.querySelectorAll('#leadTable tbody tr').forEach(row => {
    const text = row.innerText.toLowerCase();
    const rowStatus = row.getAttribute('data-status') || '';

    const matchesSearch = !search || text.includes(search);
    const matchesStatus = !status || rowStatus === status;

    const isMatch = matchesSearch && matchesStatus;
    row.style.display = isMatch ? '' : 'none';
    if (isMatch) visible++;
  });

  const hint = document.getElementById('leadCountHint');
  if (hint) hint.textContent = `${visible} leads found`;
  const pageInfo = document.getElementById('leadPaginationInfo');
  if (pageInfo) pageInfo.textContent = `Showing ${visible > 0 ? '1–' + visible : '0'} of ${visible} leads`;
}

function clearLeadListFilters() {
  const s = document.getElementById('leadListSearch');
  const st = document.getElementById('leadStatusFilter');
  if (s) s.value = '';
  if (st) st.value = '';
  filterLeadListTable();
}

// Sidebar toggle for small screens
function toggleSidebar() {
  document.querySelector('.sidebar')?.classList.toggle('show-mobile');
}

// ===== Add / Edit Lead modal (shared by Lead List and Assigned List) =====

function setLeadFormField(fieldName, value) {
  const el = document.querySelector(`#leadForm [name="${fieldName}"]`);
  if (el) el.value = value ?? '';
}

// Opens the modal blank, ready to create a brand-new lead.
function openAddLeadModal() {
  const form = document.getElementById('leadForm');
  if (form) form.reset();
  setLeadFormField('Input.LeadId', '');
  document.getElementById('leadModalTitle').innerText = 'Add New Lead';
  document.getElementById('leadModalSubtitle').innerText = 'Capture full lead details to add them to your pipeline';
  document.getElementById('leadSaveBtnLabel').innerText = 'Save Lead';
  openModal('addLeadModal');
}

// Opens the modal pre-filled with an existing lead's data for editing.
function openEditLeadModal(lead) {
  setLeadFormField('Input.LeadId', lead.leadId);
  setLeadFormField('Input.LeadName', lead.leadName);
  setLeadFormField('Input.CompanyName', lead.companyName);
  setLeadFormField('Input.MobileNumber', lead.mobileNumber);
  setLeadFormField('Input.Email', lead.email);
  setLeadFormField('Input.Designation', lead.designation);
  setLeadFormField('Input.Location', lead.location);
  setLeadFormField('Input.Source', lead.source);
  setLeadFormField('Input.LeadStatus', lead.leadStatus);
  setLeadFormField('Input.AssignedTo', lead.assignedTo);
  setLeadFormField('Input.Priority', lead.priority);
  setLeadFormField('Input.FollowUpDate', lead.followUpDate);
  setLeadFormField('Input.Remarks', lead.remarks);

  document.getElementById('leadModalTitle').innerText = 'Edit Lead';
  document.getElementById('leadModalSubtitle').innerText = `Update details for ${lead.leadName} (${lead.leadId})`;
  document.getElementById('leadSaveBtnLabel').innerText = 'Update Lead';
  openModal('addLeadModal');
}

// Confirmation guard for delete forms.
function confirmDeleteLead(leadName) {
  return confirm(`Delete lead "${leadName}"? This action cannot be undone.`);
}

// ===== Lead Details: Tab interface (Call / Follow-up / Requirement / Assign) =====

// Switches the visible tab on the Lead Details page without any page reload.
function switchLeadTab(tabName) {
  if (!tabName) return;

  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.classList.toggle('active', btn.getAttribute('data-tab') === tabName);
  });

  document.querySelectorAll('.tab-panel').forEach(panel => {
    panel.classList.toggle('active', panel.id === `tab-${tabName}`);
  });
}

// Shows/hides the inline "Add Requirement" form within the Requirement tab.
function toggleRequirementForm() {
  const el = document.getElementById('addRequirementForm');
  if (!el) return;
  el.style.display = (el.style.display === 'none' || !el.style.display) ? 'block' : 'none';
}

// Dynamic Requirement Items in Table Format
function addRequirementItem() {
  const tbody = document.getElementById('requirementItemsTbody');
  if (!tbody) return;

  const emptyHint = document.getElementById('reqItemsEmptyHint');
  if (emptyHint) emptyHint.style.display = 'none';

  const row = document.createElement('tr');
  row.className = 'req-item-row';

  row.innerHTML = `
    <td style="text-align:center;font-weight:700;color:var(--brand-700);vertical-align:middle;">
      <span class="req-sno-badge">1</span>
    </td>
    <td>
      <input type="text" name="category" class="req-input" placeholder="Category" />
    </td>
    <td>
      <input type="text" name="items" class="req-input" placeholder="Item" />
    </td>
    <td>
      <input type="text" name="location" class="req-input" placeholder="Location" />
    </td>
    <td>
      <input type="text" inputmode="decimal" name="h1" class="req-input req-dim-input" placeholder="H1" />
    </td>
    <td>
      <input type="text" inputmode="decimal" name="h2" class="req-input req-dim-input" placeholder="H2" />
    </td>
    <td>
      <input type="text" inputmode="decimal" name="h3" class="req-input req-dim-input" placeholder="H3" />
    </td>
    <td>
      <input type="text" inputmode="decimal" name="w1" class="req-input req-dim-input" placeholder="W1" />
    </td>
    <td>
      <input type="text" inputmode="decimal" name="w2" class="req-input req-dim-input" placeholder="W2" />
    </td>
    <td>
      <input type="text" inputmode="decimal" name="w3" class="req-input req-dim-input" placeholder="W3" />
    </td>
    <td>
      <input type="text" name="types" class="req-input" placeholder="Types" />
    </td>
    <td>
      <input type="text" name="topology" class="req-input" placeholder="Topology" />
    </td>
    <td>
      <input type="text" name="glass" class="req-input" placeholder="Glass" />
    </td>
    <td style="text-align:center;vertical-align:middle;">
      <div class="req-img-actions-wrap">
        <!-- 1. Existing Camera Button (Kept unchanged in look) -->
        <button type="button" class="req-file-label req-cam-btn" title="Capture photo with Camera" onclick="openRequirementCamera(this)">
          <i class="bi bi-camera-fill"></i> <span class="req-file-text">+</span>
        </button>

        <!-- 2. Separate Upload Photo Option -->
        <label class="req-file-label req-upload-btn" title="Upload Photo (JPG, JPEG, PNG, WEBP)">
          <i class="bi bi-cloud-arrow-up-fill"></i> <span class="req-file-text">Upload</span>
          <input type="file" class="req-file-input" accept=".jpg,.jpeg,.png,.webp,.gif" onchange="handleReqImageSelect(this)" style="display:none;" />
        </label>

        <!-- Hidden input for base64 / captured camera image -->
        <input type="hidden" name="requirementImageData" class="req-image-data" value="" />

        <!-- 3. Small Image Preview Thumbnail & Remove/Change controls -->
        <div class="req-img-preview-box" style="display:none;">
          <img src="" alt="Preview" class="req-thumb-img" onclick="previewRequirementFullImg(this.src)" title="Click to view full preview" />
          <button type="button" class="req-img-remove-btn" title="Remove selected image" onclick="removeRequirementImage(this)">
            <i class="bi bi-x"></i>
          </button>
        </div>
      </div>
    </td>
    <td style="text-align:center;vertical-align:middle;">
      <button type="button" class="fin-remove-row" style="color:#c81e1e;background:transparent;border:none;cursor:pointer;font-size:14px;padding:4px;" title="Remove this item" onclick="removeRequirementItem(this)">
        <i class="bi bi-trash"></i>
      </button>
    </td>
  `;

  tbody.appendChild(row);
  renumberRequirementItems();
}

function removeRequirementItem(btn) {
  const row = btn.closest('.req-item-row');
  if (row) {
    row.remove();
    renumberRequirementItems();
  }
}

function renumberRequirementItems() {
  const tbody = document.getElementById('requirementItemsTbody');
  if (!tbody) return;
  const rows = tbody.querySelectorAll('.req-item-row');

  const emptyHint = document.getElementById('reqItemsEmptyHint');
  if (emptyHint) {
    emptyHint.style.display = rows.length === 0 ? 'block' : 'none';
  }

  rows.forEach((row, index) => {
    const num = index + 1;
    const badge = row.querySelector('.req-sno-badge');
    if (badge) {
      badge.textContent = num;
    }
    const fileInput = row.querySelector('.req-file-input');
    if (fileInput) {
      fileInput.name = `requirementImage_${index}`;
    }
    const dataInput = row.querySelector('.req-image-data');
    if (dataInput) {
      dataInput.name = `requirementImageData_${index}`;
    }
  });
}

function handleReqImageSelect(input) {
  const row = input.closest('.req-item-row');
  if (!row) return;

  const uploadLabel = input.closest('.req-upload-btn');
  const previewBox = row.querySelector('.req-img-preview-box');
  const thumbImg = row.querySelector('.req-thumb-img');
  const dataInput = row.querySelector('.req-image-data');

  if (input.files && input.files.length > 0) {
    const file = input.files[0];
    const fileName = file.name;
    const ext = fileName.split('.').pop().toLowerCase();
    const validExts = ['jpg', 'jpeg', 'png', 'webp', 'gif'];

    if (!validExts.includes(ext) && !file.type.startsWith('image/')) {
      alert('Please select a valid image format (JPG, JPEG, PNG, or WEBP).');
      input.value = '';
      return;
    }

    const reader = new FileReader();
    reader.onload = function (e) {
      const dataUrl = e.target.result;
      if (thumbImg) thumbImg.src = dataUrl;
      if (dataInput) dataInput.value = dataUrl;
      if (previewBox) previewBox.style.display = 'inline-flex';
      if (uploadLabel) {
        uploadLabel.classList.add('has-file');
        uploadLabel.title = fileName;
      }
    };
    reader.readAsDataURL(file);
  }
}

function removeRequirementImage(btn) {
  const row = btn.closest('.req-item-row');
  if (!row) return;

  const previewBox = row.querySelector('.req-img-preview-box');
  const thumbImg = row.querySelector('.req-thumb-img');
  const fileInput = row.querySelector('.req-file-input');
  const dataInput = row.querySelector('.req-image-data');
  const camBtn = row.querySelector('.req-cam-btn');
  const uploadBtn = row.querySelector('.req-upload-btn');

  if (thumbImg) thumbImg.src = '';
  if (previewBox) previewBox.style.display = 'none';
  if (fileInput) fileInput.value = '';
  if (dataInput) dataInput.value = '';
  if (camBtn) camBtn.classList.remove('has-file');
  if (uploadBtn) {
    uploadBtn.classList.remove('has-file');
    uploadBtn.title = 'Upload Photo (JPG, JPEG, PNG, WEBP)';
  }
}

// ===== Camera Capture Modal Handlers =====
let activeReqRowForCamera = null;
let reqCameraStream = null;
let capturedCameraDataUrl = '';

function openRequirementCamera(btn) {
  activeReqRowForCamera = btn.closest('.req-item-row');
  const modal = document.getElementById('reqCameraModal');
  if (!modal) return;

  const video = document.getElementById('reqCameraVideo');
  const photoPreview = document.getElementById('reqCameraPhotoPreview');
  const captureBtn = document.getElementById('reqCameraCaptureBtn');
  const retakeBtn = document.getElementById('reqCameraRetakeBtn');
  const useBtn = document.getElementById('reqCameraUseBtn');
  const errBox = document.getElementById('reqCameraErrorBox');

  if (photoPreview) photoPreview.style.display = 'none';
  if (video) video.style.display = 'block';
  if (captureBtn) captureBtn.style.display = 'inline-flex';
  if (retakeBtn) retakeBtn.style.display = 'none';
  if (useBtn) useBtn.style.display = 'none';
  if (errBox) errBox.style.display = 'none';

  modal.classList.add('active');
  modal.style.display = 'flex';

  startReqCameraStream();
}

async function startReqCameraStream() {
  const video = document.getElementById('reqCameraVideo');
  const errBox = document.getElementById('reqCameraErrorBox');
  const captureBtn = document.getElementById('reqCameraCaptureBtn');

  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    if (errBox) {
      errBox.innerHTML = '<i class="bi bi-exclamation-triangle-fill"></i> Camera API is not supported in this browser or requires a secure context (HTTPS/localhost). Please use the Upload Photo option.';
      errBox.style.display = 'block';
    }
    if (captureBtn) captureBtn.style.display = 'none';
    return;
  }

  try {
    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } }
      });
    } catch {
      stream = await navigator.mediaDevices.getUserMedia({ video: true });
    }

    reqCameraStream = stream;
    if (video) {
      video.srcObject = stream;
      video.play();
    }
  } catch (err) {
    console.error('Camera access error:', err);
    if (errBox) {
      errBox.innerHTML = '<i class="bi bi-camera-video-off-fill"></i> Camera access denied or no camera device found. Please grant permissions or use the Upload Photo option.';
      errBox.style.display = 'block';
    }
    if (captureBtn) captureBtn.style.display = 'none';
  }
}

function stopReqCameraStream() {
  if (reqCameraStream) {
    reqCameraStream.getTracks().forEach(track => track.stop());
    reqCameraStream = null;
  }
  const video = document.getElementById('reqCameraVideo');
  if (video) {
    video.srcObject = null;
  }
}

function closeReqCameraModal() {
  stopReqCameraStream();
  const modal = document.getElementById('reqCameraModal');
  if (modal) {
    modal.classList.remove('active');
    modal.style.display = 'none';
  }
  activeReqRowForCamera = null;
  capturedCameraDataUrl = '';
}

function captureReqPhoto() {
  const video = document.getElementById('reqCameraVideo');
  const canvas = document.getElementById('reqCameraCanvas');
  const photoPreview = document.getElementById('reqCameraPhotoPreview');
  const captureBtn = document.getElementById('reqCameraCaptureBtn');
  const retakeBtn = document.getElementById('reqCameraRetakeBtn');
  const useBtn = document.getElementById('reqCameraUseBtn');

  if (!video || !canvas) return;

  const width = video.videoWidth || 640;
  const height = video.videoHeight || 480;
  canvas.width = width;
  canvas.height = height;

  const ctx = canvas.getContext('2d');
  ctx.drawImage(video, 0, 0, width, height);

  capturedCameraDataUrl = canvas.toDataURL('image/jpeg', 0.92);

  if (photoPreview) {
    photoPreview.src = capturedCameraDataUrl;
    photoPreview.style.display = 'block';
  }
  video.style.display = 'none';

  if (captureBtn) captureBtn.style.display = 'none';
  if (retakeBtn) retakeBtn.style.display = 'inline-flex';
  if (useBtn) useBtn.style.display = 'inline-flex';
}

function retakeReqPhoto() {
  const video = document.getElementById('reqCameraVideo');
  const photoPreview = document.getElementById('reqCameraPhotoPreview');
  const captureBtn = document.getElementById('reqCameraCaptureBtn');
  const retakeBtn = document.getElementById('reqCameraRetakeBtn');
  const useBtn = document.getElementById('reqCameraUseBtn');

  if (photoPreview) photoPreview.style.display = 'none';
  if (video) video.style.display = 'block';
  if (captureBtn) captureBtn.style.display = 'inline-flex';
  if (retakeBtn) retakeBtn.style.display = 'none';
  if (useBtn) useBtn.style.display = 'none';
}

function useCapturedReqPhoto() {
  if (!activeReqRowForCamera || !capturedCameraDataUrl) {
    closeReqCameraModal();
    return;
  }

  // Set hidden data input for base64 submission
  const dataInput = activeReqRowForCamera.querySelector('.req-image-data');
  if (dataInput) {
    dataInput.value = capturedCameraDataUrl;
  }

  // Clear file input so base64 takes precedence
  const fileInput = activeReqRowForCamera.querySelector('.req-file-input');
  if (fileInput) {
    fileInput.value = '';
  }

  // Set thumbnail preview
  const thumbImg = activeReqRowForCamera.querySelector('.req-thumb-img');
  const previewBox = activeReqRowForCamera.querySelector('.req-img-preview-box');
  if (thumbImg) {
    thumbImg.src = capturedCameraDataUrl;
  }
  if (previewBox) {
    previewBox.style.display = 'inline-flex';
  }

  const camBtn = activeReqRowForCamera.querySelector('.req-cam-btn');
  if (camBtn) camBtn.classList.add('has-file');

  closeReqCameraModal();
}

function previewRequirementFullImg(src) {
  if (!src) return;
  const modal = document.getElementById('reqImageZoomModal');
  const img = document.getElementById('reqZoomImg');
  if (modal && img) {
    img.src = src;
    modal.classList.add('active');
    modal.style.display = 'flex';
  }
}

function closeReqZoomModal() {
  const modal = document.getElementById('reqImageZoomModal');
  if (modal) {
    modal.classList.remove('active');
    modal.style.display = 'none';
  }
}

window.toggleRequirementForm = toggleRequirementForm;
window.addRequirementItem = addRequirementItem;
window.removeRequirementItem = removeRequirementItem;
window.renumberRequirementItems = renumberRequirementItems;
window.handleReqImageSelect = handleReqImageSelect;
window.removeRequirementImage = removeRequirementImage;
window.openRequirementCamera = openRequirementCamera;
window.closeReqCameraModal = closeReqCameraModal;
window.captureReqPhoto = captureReqPhoto;
window.retakeReqPhoto = retakeReqPhoto;
window.useCapturedReqPhoto = useCapturedReqPhoto;
window.previewRequirementFullImg = previewRequirementFullImg;
window.closeReqZoomModal = closeReqZoomModal;

// ===== Finance tab: Estimation / Quotation / Proposal sub-tabs =====

// Switches the visible Finance sub-tab (Estimation | Quotation | Proposal). Independent of switchLeadTab.
function switchFinanceSubTab(name) {
  if (!name) return;
  document.querySelectorAll('.fin-subtab-btn').forEach(btn => {
    btn.classList.toggle('active', btn.getAttribute('data-subtab') === name);
  });
  document.querySelectorAll('.fin-subpanel').forEach(panel => {
    panel.classList.toggle('active', panel.id === `finsub-${name}`);
  });
}

// Expands/collapses the read-only "View" detail block for a saved finance record.
function toggleFinanceDetail(detailId) {
  const el = document.getElementById(detailId);
  if (el) el.classList.toggle('open');
}

// Appends a new blank Item/Service row to an Estimation or Quotation items table.
function addFinanceItemRow(tbodyId) {
  const tbody = document.getElementById(tbodyId);
  if (!tbody) return;
  const row = document.createElement('tr');
  row.className = 'fin-item-row';
  row.innerHTML =
    '<td><input type="text" name="itemName" placeholder="Item / service name" oninput="recalcFinanceTotals(\'' + tbodyId + '\')" /></td>' +
    '<td><input type="number" name="itemQty" step="0.01" min="0" value="1" oninput="recalcFinanceTotals(\'' + tbodyId + '\')" /></td>' +
    '<td><input type="number" name="itemRate" step="0.01" min="0" value="0" oninput="recalcFinanceTotals(\'' + tbodyId + '\')" /></td>' +
    '<td class="fin-item-amount">0.00</td>' +
    '<td><button type="button" class="fin-remove-row" onclick="this.closest(\'tr\').remove(); recalcFinanceTotals(\'' + tbodyId + '\')"><i class="bi bi-trash"></i></button></td>';
  tbody.appendChild(row);
}

// Recomputes each row's Amount plus the Subtotal / Total preview for an Estimation or Quotation form.
// (Authoritative totals are still recalculated server-side on save.)
function recalcFinanceTotals(tbodyId) {
  const tbody = document.getElementById(tbodyId);
  if (!tbody) return;
  const prefix = tbodyId.replace('ItemsBody', '');

  let subtotal = 0;
  tbody.querySelectorAll('.fin-item-row').forEach(row => {
    const qty = parseFloat(row.querySelector('input[name=itemQty]').value) || 0;
    const rate = parseFloat(row.querySelector('input[name=itemRate]').value) || 0;
    const amt = qty * rate;
    row.querySelector('.fin-item-amount').textContent = amt.toFixed(2);
    subtotal += amt;
  });

  const subtotalEl = document.getElementById(prefix + 'SubtotalDisplay');
  if (subtotalEl) subtotalEl.textContent = subtotal.toFixed(2);

  const discountInput = document.getElementById(prefix + 'Discount');
  const taxInput = document.getElementById(prefix + 'Tax');
  const discount = discountInput ? (parseFloat(discountInput.value) || 0) : 0;
  const tax = taxInput ? (parseFloat(taxInput.value) || 0) : 0;

  const totalEl = document.getElementById(prefix + 'TotalDisplay');
  if (totalEl) totalEl.textContent = (subtotal - discount + tax).toFixed(2);
}

// Resets and opens an Estimation/Quotation/Proposal form for creating a brand-new record.
function newFinanceForm(kind) {
  const form = document.getElementById(kind + 'Form');
  if (!form) return;
  form.style.display = 'block';
  document.getElementById(kind + 'EditingNumber').value = '';
  document.getElementById(kind + 'FormTitle').textContent = 'New ' + capitalize(kind);
  document.getElementById(kind + 'NumberDisplay').textContent = 'Auto-generated on save';

  form.querySelectorAll('input[type=text], input[type=number], input[type=date], textarea').forEach(el => {
    if (el.name !== 'leadId') el.value = '';
  });
  const dateEl = document.getElementById(kind + 'Date');
  if (dateEl) dateEl.value = new Date().toISOString().slice(0, 10);
  const statusEl = document.getElementById(kind + 'Status');
  if (statusEl) statusEl.value = 'Draft';

  resetFinanceAttachment(kind);

  const tbody = document.getElementById(kind + 'ItemsBody');
  if (tbody) {
    tbody.innerHTML = '';
    addFinanceItemRow(kind + 'ItemsBody');
    recalcFinanceTotals(kind + 'ItemsBody');
  }

  form.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

// Hides an Estimation/Quotation/Proposal form (Cancel button).
function cancelFinanceForm(kind) {
  const form = document.getElementById(kind + 'Form');
  if (form) form.style.display = 'none';
}

// Populates an Estimation/Quotation/Proposal form with a saved record's data for editing.
// Reads the record's JSON payload embedded on the page (script#{kind}-json-{number}).
function editFinanceRecord(kind, number) {
  const dataEl = document.getElementById(kind + '-json-' + number);
  if (!dataEl) return;
  const d = JSON.parse(dataEl.textContent);

  const form = document.getElementById(kind + 'Form');
  form.style.display = 'block';
  document.getElementById(kind + 'EditingNumber').value = d.Number;
  document.getElementById(kind + 'FormTitle').textContent = 'Edit ' + capitalize(kind) + ' — ' + d.Number;
  document.getElementById(kind + 'NumberDisplay').textContent = d.Number;

  setVal(kind + 'Date', d.Date);
  setVal(kind + 'ProjectRequirement', d.ProjectRequirement);
  setVal(kind + 'Terms', d.TermsConditions);
  setVal(kind + 'Notes', d.Notes || d.Description || '');
  setVal(kind + 'Status', d.Status);
  setVal(kind + 'ValidUntil', d.ValidUntil);
  setVal(kind + 'Discount', d.Discount);
  setVal(kind + 'Tax', d.Tax);

  // Proposal-only fields
  setVal(kind + 'Title', d.Title);
  setVal(kind + 'ScopeOfWork', d.ScopeOfWork);
  setVal(kind + 'Description', d.Description);
  setVal(kind + 'Deliverables', d.Deliverables);
  setVal(kind + 'Timeline', d.Timeline);
  setVal(kind + 'Budget', d.Budget);

  resetFinanceAttachment(kind);
  if (d.AttachmentPath) {
    const currentBox = document.getElementById(kind + 'CurrentAttachment');
    const currentLink = document.getElementById(kind + 'CurrentAttachmentLink');
    if (currentBox && currentLink) {
      currentLink.href = d.AttachmentPath;
      currentLink.textContent = d.AttachmentFileName || 'Attached file';
      currentBox.style.display = 'flex';
    }
  }

  const tbody = document.getElementById(kind + 'ItemsBody');
  if (tbody) {
    tbody.innerHTML = '';
    (d.Items || []).forEach(it => {
      addFinanceItemRow(kind + 'ItemsBody');
      const row = tbody.lastElementChild;
      row.querySelector('input[name=itemName]').value = it.ItemName;
      row.querySelector('input[name=itemQty]').value = it.Quantity;
      row.querySelector('input[name=itemRate]').value = it.Rate;
    });
    if ((d.Items || []).length === 0) addFinanceItemRow(kind + 'ItemsBody');
    recalcFinanceTotals(kind + 'ItemsBody');
  }

  form.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function setVal(id, val) {
  const el = document.getElementById(id);
  if (el && val !== undefined && val !== null) el.value = val;
}

function capitalize(s) {
  return s.charAt(0).toUpperCase() + s.slice(1);
}

// Shows the picked file's name next to the "Choose File" button on a Finance form.
function previewFinanceAttachment(input, kind) {
  const nameEl = document.getElementById(kind + 'AttachmentFileName');
  if (!nameEl) return;
  nameEl.textContent = (input.files && input.files.length > 0) ? input.files[0].name : 'No file chosen';
}

// Clears the file input and any "current attachment" preview on a Finance form
// (called when opening a blank form and before populating an edit).
function resetFinanceAttachment(kind) {
  const fileInput = document.getElementById(kind + 'Attachment');
  if (fileInput) fileInput.value = '';
  const nameEl = document.getElementById(kind + 'AttachmentFileName');
  if (nameEl) nameEl.textContent = 'No file chosen';
  const currentBox = document.getElementById(kind + 'CurrentAttachment');
  if (currentBox) currentBox.style.display = 'none';
}

// Sidebar dropdown (Finance): opens/closes the submenu and keeps aria-expanded in sync.
function toggleSidebarGroup(btn) {
  const group = btn.closest('.sidebar-group');
  if (!group) return;
  const isOpen = group.classList.toggle('open');
  btn.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
}
