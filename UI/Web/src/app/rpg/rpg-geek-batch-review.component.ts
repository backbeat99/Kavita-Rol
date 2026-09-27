import {ChangeDetectionStrategy, Component, computed, inject, OnInit, signal} from '@angular/core';
import {CommonModule} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {ActivatedRoute, RouterLink} from '@angular/router';
import {TranslocoDirective, translate} from '@jsverse/transloco';
import {ToastrService} from '@openng/ngx-toastr';
import {AccountService} from '../_services/account.service';
import {ImageService} from '../_services/image.service';
import {SeriesService} from '../_services/series.service';
import {VolumeService} from '../_services/volume.service';
import {
  ApplyRpgGeekCandidate,
  RpgGeekCandidatePreview,
  RpgGeekMatchStatus,
  RpgGeekSearchResult,
  RpgMaterialType
} from '../_models/rpg/rpg-catalog';
import {Volume} from '../_models/volume';
import {getResolvedData} from '../../libs/route-util';

type ReplaceField = 'replaceTitle' | 'replaceSummary' | 'replaceYear' | 'replaceWriters' | 'replacePublishers' | 'replaceCover';

type ReplacementFlags = Record<ReplaceField, boolean>;

interface CandidateReviewItem {
  volume: Volume;
  status: RpgGeekMatchStatus;
  candidates: RpgGeekSearchResult[];
  isSearching: boolean;
  hasSearched: boolean;
  searchError: string | null;
  selectedProductId: number | null;
  preview: RpgGeekCandidatePreview | null;
  isPreviewing: boolean;
  selectedForBatch: boolean;
  replacements: ReplacementFlags;
}

const emptyReplacements = (): ReplacementFlags => ({
  replaceTitle: false,
  replaceSummary: false,
  replaceYear: false,
  replaceWriters: false,
  replacePublishers: false,
  replaceCover: false
});

@Component({
  selector: 'app-rpg-geek-batch-review',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslocoDirective, RouterLink],
  templateUrl: './rpg-geek-batch-review.component.html',
  styleUrl: './rpg-geek-batch-review.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RpgGeekBatchReviewComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly seriesService = inject(SeriesService);
  private readonly volumeService = inject(VolumeService);
  private readonly accountService = inject(AccountService);
  private readonly toastr = inject(ToastrService);
  protected readonly imageService = inject(ImageService);

  readonly library = getResolvedData(this.route, 'library');
  readonly series = getResolvedData(this.route, 'series');
  readonly isAdmin = this.accountService.hasAdminRole;
  readonly RpgGeekMatchStatus = RpgGeekMatchStatus;
  readonly items = signal<CandidateReviewItem[]>([]);
  readonly isLoading = signal(true);
  readonly isApplying = signal(false);
  readonly batchError = signal<string | null>(null);
  readonly linkedCount = signal(0);
  readonly selectedCount = computed(() => this.items().filter(item => item.selectedForBatch).length);
  readonly searchingCount = computed(() => this.items().filter(item => item.isSearching).length);
  readonly hasSearched = computed(() => this.items().some(item => item.hasSearched));
  readonly previewingCount = computed(() => this.items().filter(item => item.isPreviewing).length);
  readonly readyCount = computed(() => this.items().filter(item => item.selectedForBatch && !!item.preview).length);
  readonly batchLimit = 50;

  ngOnInit(): void {
    if (!this.isAdmin()) {
      this.isLoading.set(false);
      return;
    }
    this.loadPublications();
  }

  requeryAll(): void {
    this.searchAll(this.hasSearched());
  }

  retryLoad(): void {
    this.loadPublications();
  }

  searchItem(volumeId: number, forceRefresh = false): void {
    const item = this.findItem(volumeId);
    if (!item || item.isSearching || item.isPreviewing) return;
    this.updateItem(volumeId, {
      isSearching: true,
      hasSearched: true,
      searchError: null,
      selectedProductId: null,
      preview: null,
      selectedForBatch: false,
      replacements: emptyReplacements()
    });
    this.volumeService.searchRpgGeekCandidates(volumeId, item.volume.name, forceRefresh).subscribe({
      next: result => {
        this.updateItem(volumeId, {
          status: result.status,
          candidates: result.candidates ?? [],
          isSearching: false,
          searchError: result.succeeded ? null : this.errorForCode(result.error)
        });
      },
      error: error => this.updateItem(volumeId, {
        isSearching: false,
        searchError: this.requestMessage(error)
      })
    });
  }

  selectCandidate(volumeId: number, productId: string | number): void {
    const id = Number(productId);
    const item = this.findItem(volumeId);
    if (!item || !Number.isInteger(id) || id <= 0) {
      this.updateItem(volumeId, {selectedProductId: null, preview: null, selectedForBatch: false});
      return;
    }

    this.updateItem(volumeId, {
      selectedProductId: id,
      preview: null,
      isPreviewing: true,
      selectedForBatch: false,
      replacements: emptyReplacements(),
      searchError: null
    });
    this.loadPreview(volumeId, id, false);
  }

  refreshPreview(volumeId: number): void {
    const item = this.findItem(volumeId);
    if (!item?.selectedProductId || item.isPreviewing) return;
    this.updateItem(volumeId, {preview: null, isPreviewing: true, selectedForBatch: false});
    this.loadPreview(volumeId, item.selectedProductId, true);
  }

  setReplacement(volumeId: number, field: ReplaceField, checked: boolean): void {
    const item = this.findItem(volumeId);
    if (!item) return;
    this.updateItem(volumeId, {replacements: {...item.replacements, [field]: checked}});
  }

  setSelectedForBatch(volumeId: number, checked: boolean): void {
    const item = this.findItem(volumeId);
    if (!item?.preview || item.status !== RpgGeekMatchStatus.Candidate) return;
    if (checked && !item.selectedForBatch && this.selectedCount() >= this.batchLimit) {
      this.toastr.info(translate('rpg-geek-batch-review.selection-limit', {count: this.batchLimit}));
      return;
    }
    this.updateItem(volumeId, {selectedForBatch: checked});
  }

  hasExistingValue(item: CandidateReviewItem, field: ReplaceField): boolean {
    switch (field) {
      case 'replaceTitle': return !!item.volume.name;
      case 'replaceSummary': return !!item.volume.summary;
      case 'replaceYear': return item.volume.rpgPublicationYear !== null && item.volume.rpgPublicationYear !== undefined;
      case 'replaceWriters': return (item.volume.rpgWriters?.length ?? 0) > 0;
      case 'replacePublishers': return (item.volume.rpgPublishers?.length ?? 0) > 0;
      case 'replaceCover': return !!item.volume.coverImage;
    }
  }

  isFieldLocked(item: CandidateReviewItem, field: ReplaceField): boolean {
    switch (field) {
      case 'replaceTitle': return item.volume.nameLocked;
      case 'replaceSummary': return item.volume.summaryLocked;
      case 'replaceYear': return item.volume.rpgPublicationYearLocked;
      case 'replaceWriters': return item.volume.rpgWritersLocked;
      case 'replacePublishers': return item.volume.rpgPublishersLocked;
      case 'replaceCover': return item.volume.coverImageLocked;
    }
  }

  confirmBatch(): void {
    const selected = this.items().filter(item => item.selectedForBatch && item.preview && item.selectedProductId);
    if (selected.length === 0 || this.isApplying()) return;

    const requests: ApplyRpgGeekCandidate[] = selected.map(item => ({
      volumeId: item.volume.id,
      productId: item.selectedProductId!,
      previewFingerprint: item.preview!.fingerprint,
      ...item.replacements
    }));
    this.isApplying.set(true);
    this.batchError.set(null);
    this.volumeService.applyRpgGeekCandidatesBatch({seriesId: this.series().id, items: requests}).subscribe({
      next: succeeded => {
        if (!succeeded) {
          this.batchError.set(translate('rpg-geek-batch-review.errors.apply-failed'));
          return;
        }
        this.toastr.success(translate('rpg-geek-batch-review.batch-linked', {count: selected.length}));
        this.loadPublications();
      },
      error: error => {
        this.updateChangedPreviews(error);
        this.batchError.set(this.requestMessage(error));
        this.isApplying.set(false);
      },
      complete: () => this.isApplying.set(false)
    });
  }

  private loadPublications(): void {
    this.isLoading.set(true);
    this.batchError.set(null);
    this.seriesService.getSeriesDetail(this.series().id).subscribe({
      next: detail => {
        const publications = detail.volumes.filter(volume =>
          volume.rpgMaterialType >= RpgMaterialType.CoreManual &&
          volume.rpgMaterialType <= RpgMaterialType.OtherPublication);
        const unlinked = publications.filter(volume => !volume.rpgGeekId);
        this.linkedCount.set(publications.length - unlinked.length);
        this.items.set(unlinked.map(volume => ({
          volume,
          status: volume.rpgGeekMatchStatus,
          candidates: [],
          isSearching: false,
          hasSearched: false,
          searchError: null,
          selectedProductId: null,
          preview: null,
          isPreviewing: false,
          selectedForBatch: false,
          replacements: emptyReplacements()
        })));
        this.isLoading.set(false);
      },
      error: () => {
        this.batchError.set(translate('rpg-geek-batch-review.errors.load-failed'));
        this.isLoading.set(false);
      }
    });
  }

  private searchAll(forceRefresh: boolean): void {
    const currentItems = this.items();
    if (currentItems.length === 0 || this.searchingCount() > 0 || this.previewingCount() > 0 || this.isApplying()) return;
    this.batchError.set(null);
    for (const item of currentItems) this.searchItem(item.volume.id, forceRefresh);
  }

  private loadPreview(volumeId: number, productId: number, forceRefresh: boolean): void {
    this.volumeService.previewRpgGeekCandidate(volumeId, productId, forceRefresh).subscribe({
      next: preview => this.updateItem(volumeId, {
        preview,
        isPreviewing: false,
        searchError: preview?.product ? null : translate('rpg-geek-batch-review.errors.7')
      }),
      error: error => this.updateItem(volumeId, {
        isPreviewing: false,
        searchError: this.requestMessage(error)
      })
    });
  }

  private updateChangedPreviews(error: any): void {
    const changed = error?.error?.updatedPreviews ?? error?.error?.UpdatedPreviews ?? [];
    for (const entry of changed) {
      const volumeId = entry.volumeId ?? entry.VolumeId;
      const preview = entry.preview ?? entry.Preview;
      if (volumeId && preview) this.updateItem(volumeId, {preview, selectedForBatch: false});
    }
  }

  private errorForCode(code: number): string {
    return translate(`rpg-geek-batch-review.errors.${code}`);
  }

  private requestMessage(error: any): string {
    const body = error?.error;
    const serverMessage = typeof body === 'string' ? body : body?.message ?? body?.Message;
    const code = Number(body?.error ?? body?.Error);
    return serverMessage || (Number.isInteger(code)
      ? this.errorForCode(code)
      : translate('rpg-geek-batch-review.errors.request-failed'));
  }

  private findItem(volumeId: number): CandidateReviewItem | undefined {
    return this.items().find(item => item.volume.id === volumeId);
  }

  private updateItem(volumeId: number, patch: Partial<CandidateReviewItem>): void {
    this.items.update(items => items.map(item => item.volume.id === volumeId ? {...item, ...patch} : item));
  }
}
