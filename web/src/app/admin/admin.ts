import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Permission, Product, ProductRelease, Role, UserSummary } from '../core/models';
import { UiService } from '../core/ui.service';

/** Products & releases, users, role permissions and master data. */
@Component({
  selector: 'app-admin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, MatTabsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule],
  templateUrl: './admin.html',
  styles: `
    .tab-body { padding: 12px 0; }
    .split { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; align-items: start; }
    .matrix th.perm { writing-mode: vertical-rl; transform: rotate(180deg); white-space: nowrap; font-weight: 500; height: 150px; }
    .matrix td { text-align: center; } .matrix td:first-child { text-align: left; white-space: nowrap; }
    .cell { font: inherit; padding: 5px 7px; border: 1px solid var(--border); border-radius: 4px; }
    .small { font-size: 11px; }
  `,
})
export class Admin {
  private api = inject(ApiService);
  ui = inject(UiService);
  auth = inject(AuthService);

  products = signal<Product[]>([]);
  selectedProduct = signal<Product | null>(null);
  releases = signal<ProductRelease[]>([]);
  users = signal<UserSummary[]>([]);
  roles = signal<Role[]>([]);
  permissions = signal<Permission[]>([]);
  editingUser = signal<UserSummary | null>(null);
  roleNames = computed(() => this.roles().map(r => r.name));

  newProduct = { code: '', name: '', description: '', sequenceScopeKey: '' };
  productDraft: Partial<Product> = {};
  newRelease: Partial<ProductRelease> = { isReleased: false };
  newUser = { userName: '', displayName: '', email: '', password: '', roles: [] as string[] };
  userDraft = { displayName: '', email: '', isActive: true, roles: [] as string[] };
  accessDraft = new Map<string, boolean>();
  master = { kind: 'test-types' as 'feature-types' | 'feature-statuses' | 'test-types', code: '', name: '', sortOrder: 500 };

  constructor() {
    this.loadProducts();
    if (this.auth.can('users.manage')) { this.loadUsers(); this.loadRoles(); }
  }

  // ---- products & releases ----
  loadProducts() { this.api.products().subscribe({ next: p => this.products.set(p), error: e => this.ui.error(e) }); }

  selectProduct(p: Product) {
    this.selectedProduct.set(p);
    this.productDraft = { ...p };
    this.api.releases(p.id).subscribe(r => this.releases.set(r));
  }

  async createProduct() {
    try {
      const p = await firstValueFrom(this.api.createProduct({ ...this.newProduct, sequenceScopeKey: this.newProduct.sequenceScopeKey || undefined }));
      this.newProduct = { code: '', name: '', description: '', sequenceScopeKey: '' };
      this.ui.ok(`Created ${p.name}`);
      this.loadProducts();
      this.ui.loadLookups(true);
      this.selectProduct(p);
    } catch (e) { this.ui.error(e); }
  }

  async saveProduct() {
    const p = this.selectedProduct()!;
    try { const u = await firstValueFrom(this.api.updateProduct(p.id, this.productDraft)); this.ui.ok('Saved'); this.loadProducts(); this.selectProduct(u); }
    catch (e) { this.ui.error(e); }
  }

  async createRelease() {
    const p = this.selectedProduct()!;
    try { await firstValueFrom(this.api.createRelease(p.id, this.newRelease)); this.newRelease = { isReleased: false }; this.api.releases(p.id).subscribe(r => this.releases.set(r)); }
    catch (e) { this.ui.error(e); }
  }

  async toggleReleased(r: ProductRelease) {
    try { await firstValueFrom(this.api.updateRelease(r.productId, r.id, { ...r, isReleased: !r.isReleased })); this.api.releases(r.productId).subscribe(x => this.releases.set(x)); }
    catch (e) { this.ui.error(e); }
  }

  // ---- users ----
  loadUsers() { this.api.users().subscribe({ next: u => this.users.set(u), error: e => this.ui.error(e) }); }
  loadRoles() {
    this.api.roles().subscribe({ next: r => this.roles.set(r), error: e => this.ui.error(e) });
    this.api.permissions().subscribe(p => this.permissions.set(p));
  }

  async createUser() {
    try { await firstValueFrom(this.api.createUser({ ...this.newUser, email: this.newUser.email || undefined })); this.newUser = { userName: '', displayName: '', email: '', password: '', roles: [] }; this.ui.ok('User created'); this.loadUsers(); }
    catch (e) { this.ui.error(e); }
  }

  editUser(u: UserSummary) {
    this.editingUser.set(u);
    this.userDraft = { displayName: u.displayName, email: u.email ?? '', isActive: u.isActive, roles: [...u.roles] };
    this.accessDraft = new Map(u.productAccess.map(a => [a.productId, a.canEdit]));
  }

  async saveUser() {
    const u = this.editingUser()!;
    try {
      await firstValueFrom(this.api.updateUser(u.id, { ...this.userDraft, email: this.userDraft.email || undefined }));
      await firstValueFrom(this.api.setProductAccess(u.id, [...this.accessDraft].map(([productId, canEdit]) => ({ productId, canEdit }))));
      this.ui.ok('User saved');
      this.editingUser.set(null);
      this.loadUsers();
    } catch (e) { this.ui.error(e); }
  }

  setAccess(productId: string, level: 'none' | 'view' | 'edit') {
    if (level === 'none') this.accessDraft.delete(productId); else this.accessDraft.set(productId, level === 'edit');
    this.accessDraft = new Map(this.accessDraft);
  }
  accessLevel(productId: string) { return !this.accessDraft.has(productId) ? 'none' : this.accessDraft.get(productId) ? 'edit' : 'view'; }

  async resetPassword(u: UserSummary) {
    const pw = await this.ui.prompt({ title: `Reset password for ${u.userName}`, label: 'New password', hint: 'At least 12 characters with upper case, lower case and a digit. Also clears any lockout.', confirmText: 'Reset' });
    if (!pw) return;
    try { await firstValueFrom(this.api.resetPassword(u.id, pw)); this.ui.ok('Password reset'); this.loadUsers(); } catch (e) { this.ui.error(e); }
  }

  // ---- roles ----
  has(role: Role, perm: string) { return role.permissions.includes(perm); }
  async togglePermission(role: Role, perm: string, on: boolean) {
    const codes = on ? [...role.permissions, perm] : role.permissions.filter(p => p !== perm);
    try { const updated = await firstValueFrom(this.api.setRolePermissions(role.id, codes)); this.roles.update(rs => rs.map(r => r.id === role.id ? updated : r)); }
    catch (e) { this.ui.error(e); this.loadRoles(); }
  }

  // ---- master data ----
  async saveMaster() {
    try {
      await firstValueFrom(this.api.upsertMaster(this.master.kind, { code: this.master.code, name: this.master.name, sortOrder: this.master.sortOrder }));
      this.ui.ok('Saved');
      this.master = { ...this.master, code: '', name: '' };
      await this.ui.loadLookups(true);
    } catch (e) { this.ui.error(e); }
  }
}
