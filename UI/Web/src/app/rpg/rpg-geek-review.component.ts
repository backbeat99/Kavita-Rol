import {ChangeDetectionStrategy, Component, inject, OnInit, signal} from '@angular/core';
import {CommonModule} from '@angular/common';
import {ActivatedRoute, Router, RouterLink} from '@angular/router';
import {TranslocoDirective, translate} from '@jsverse/transloco';
import {ToastrService} from '@openng/ngx-toastr';
import {AccountService} from '../_services/account.service';
import {ImageService} from '../_services/image.service';
import {VolumeService} from '../_services/volume.service';
import {
  ApplyRpgGeekCandidate,
  RpgGeekCandidatePreview,
  RpgGeekSearchResult
} from '../_models/rpg/rpg-catalog';
import {getResolvedData} from '../../libs/route-util';

type ReplaceField = 'replaceTitle' | 'replaceSummary' | 'replaceYear' | 'replaceWriters' | 'replacePublishers' | 'replaceCover';

@Component({
  selector: 'app-rpg-geek-review',
  standalone: true,
  imports: [CommonModule, TranslocoDirective, RouterLink],
  templateUrl: './rpg-geek-review.component.html',
  styleUrl: './rpg-geek-review.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RpgGeekReviewComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly volumeService = inject(VolumeService);
  private readonly accountService = inject(AccountService);
  private readonly toastr = inject(ToastrService);
  protected readonly imageService = inject(ImageService);

  readonly library = getResolvedData(this.route, 'library');
  readonly series = getResolvedData(this.route, 'series');
  readonly volume = getResolvedData(this.route, 'volume');
  readonly isAdmin = this.accountService.hasAdminRole;
  readonly query = signal(this.volume().name);
  readonly candidates = signal<RpgGeekSearchResult[]>([]);
  readonly selectedCandidate = signal<RpgGeekSearchResult | null>(null);
  readonly preview = signal<RpgGeekCandidatePreview | null>(null);
  readonly replacements = signal<Record<ReplaceField, boolean>>({
    replaceTitle: !this.volume().nameLocked,
    replaceSummary: false,
    replaceYear: false,
    replaceWriters: false,
    replacePublishers: false,
    replaceCover: false
  });
  readonly searchStatus = signal<number | null>(null);
  readonly operationError = signal<string | null>(null);
  readonly isSearching = signal(false);
  readonly isPreviewing = signal(false);
  readonly isApplying = signal(false);
  readonly hasSearched = signal(false);

  ngOnInit(): void {}

  setQuery(value: string): void { this.query.set(value); }

  search(forceRefresh = false): void {
    const query = this.query().trim();
    if (!query || this.isSearching()) return;
    this.isSearching.set(true);
    this.hasSearched.set(true);
    this.operationError.set(null);
    this.preview.set(null);
    this.selectedCandidate.set(null);
    this.volumeService.searchRpgGeekCandidates(this.volume().id, query, forceRefresh).subscribe({
      next: result => {
        this.searchStatus.set(result.status);
        this.candidates.set(result.candidates ?? []);
        if (!result.succeeded) this.operationError.set(translate(`rpg-geek-review.errors.${result.error}`));
      },
      error: error => {
        this.setRequestError(error);
        this.isSearching.set(false);
      },
      complete: () => this.isSearching.set(false)
    });
  }

  previewCandidate(candidate: RpgGeekSearchResult): void {
    this.selectedCandidate.set(candidate);
    this.loadPreview(candidate.id);
  }

  refreshPreview(): void {
    const candidate = this.selectedCandidate();
    if (!candidate || this.isPreviewing()) return;
    this.loadPreview(candidate.id, true);
  }

  previewDirectId(): void {
    const id = Number(this.query().trim());
    if (!Number.isInteger(id) || id <= 0) {
      this.operationError.set(translate('rpg-geek-review.errors.7'));
      return;
    }
    this.selectedCandidate.set({id, name: `RPGGeek #${id}`, yearPublished: null});
    this.loadPreview(id);
  }

  setReplacement(field: ReplaceField, checked: boolean): void {
    this.replacements.update(current => ({...current, [field]: checked}));
  }

  applyCandidate(): void {
    const preview = this.preview();
    const candidate = this.selectedCandidate();
    if (!preview || !candidate || this.isApplying()) return;

    const request: ApplyRpgGeekCandidate = {
      volumeId: this.volume().id,
      productId: candidate.id,
      previewFingerprint: preview.fingerprint,
      ...this.replacements()
    };

    this.isApplying.set(true);
    this.operationError.set(null);
    this.volumeService.applyRpgGeekCandidate(request).subscribe({
      next: succeeded => {
        if (!succeeded) {
          this.operationError.set(translate('rpg-geek-review.errors.9'));
          return;
        }
        this.toastr.success(translate('rpg-geek-review.linked'));
        this.router.navigate(['/library', this.library().id, 'series', this.series().id, 'volume', this.volume().id]);
      },
      error: error => {
        const refreshedPreview = error?.error?.updatedPreview ?? error?.error?.UpdatedPreview;
        if (refreshedPreview) this.preview.set(refreshedPreview as RpgGeekCandidatePreview);
        this.setRequestError(error);
        this.isApplying.set(false);
      },
      complete: () => this.isApplying.set(false)
    });
  }

  hasExistingValue(field: ReplaceField): boolean {
    switch (field) {
      case 'replaceTitle': return !!this.volume().name;
      case 'replaceSummary': return !!this.volume().summary;
      case 'replaceYear': return this.volume().rpgPublicationYear !== null && this.volume().rpgPublicationYear !== undefined;
      case 'replaceWriters': return (this.volume().rpgWriters?.length ?? 0) > 0;
      case 'replacePublishers': return (this.volume().rpgPublishers?.length ?? 0) > 0;
      case 'replaceCover': return !!this.volume().coverImage;
    }
  }

  isFieldLocked(field: ReplaceField): boolean {
    switch (field) {
      case 'replaceTitle': return this.volume().nameLocked;
      case 'replaceSummary': return this.volume().summaryLocked;
      case 'replaceYear': return this.volume().rpgPublicationYearLocked;
      case 'replaceWriters': return this.volume().rpgWritersLocked;
      case 'replacePublishers': return this.volume().rpgPublishersLocked;
      case 'replaceCover': return this.volume().coverImageLocked;
    }
  }

  private setRequestError(error: any): void {
    const body = error?.error;
    const serverMessage = typeof body === 'string' ? body : body?.message ?? body?.Message;
    const code = Number(body?.error ?? body);
    this.operationError.set(serverMessage || (Number.isInteger(code) ? translate(`rpg-geek-review.errors.${code}`) : translate('rpg-geek-review.errors.9')));
  }

  private loadPreview(productId: number, forceRefresh = false): void {
    this.isPreviewing.set(true);
    this.operationError.set(null);
    this.preview.set(null);
    this.volumeService.previewRpgGeekCandidate(this.volume().id, productId, forceRefresh).subscribe({
      next: result => {
        this.preview.set(result);
        if (!result?.product) this.operationError.set(translate('rpg-geek-review.errors.7'));
        this.replacements.set({
          replaceTitle: !this.volume().nameLocked,
          replaceSummary: false,
          replaceYear: false,
          replaceWriters: false,
          replacePublishers: false,
          replaceCover: false
        });
      },
      error: error => {
        this.setRequestError(error);
        this.isPreviewing.set(false);
      },
      complete: () => this.isPreviewing.set(false)
    });
  }
}
