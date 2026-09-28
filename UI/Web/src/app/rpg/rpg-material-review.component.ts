import {ChangeDetectionStrategy, Component, computed, inject, OnInit, signal} from '@angular/core';
import {HttpErrorResponse} from '@angular/common/http';
import {CommonModule} from '@angular/common';
import {FormsModule} from '@angular/forms';
import {ActivatedRoute, RouterLink} from '@angular/router';
import {TranslocoDirective, translate} from '@jsverse/transloco';
import {ToastrService} from '@openng/ngx-toastr';
import {AccountService} from '../_services/account.service';
import {ImageService} from '../_services/image.service';
import {SeriesService} from '../_services/series.service';
import {VolumeService} from '../_services/volume.service';
import {SeriesDetail} from '../_models/series-detail/series-detail';
import {Volume} from '../_models/volume';
import {RpgMaterialType, RpgMaterialTypeUpdate} from '../_models/rpg/rpg-catalog';
import {getResolvedData} from '../../libs/route-util';

@Component({
  selector: 'app-rpg-material-review',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslocoDirective, RouterLink],
  templateUrl: './rpg-material-review.component.html',
  styleUrl: './rpg-material-review.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RpgMaterialReviewComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly seriesService = inject(SeriesService);
  private readonly volumeService = inject(VolumeService);
  private readonly accountService = inject(AccountService);
  private readonly toastr = inject(ToastrService);
  protected readonly imageService = inject(ImageService);

  readonly library = getResolvedData(this.route, 'library');
  readonly series = getResolvedData(this.route, 'series');
  readonly isAdmin = this.accountService.hasAdminRole;
  readonly RpgMaterialType = RpgMaterialType;
  readonly typeOptions = [
    RpgMaterialType.Unclassified,
    RpgMaterialType.CoreManual,
    RpgMaterialType.Manual,
    RpgMaterialType.Adventure,
    RpgMaterialType.Supplement,
    RpgMaterialType.OtherPublication,
    RpgMaterialType.Map,
    RpgMaterialType.CharacterSheet,
    RpgMaterialType.GameAid,
    RpgMaterialType.CardsAndTokens,
    RpgMaterialType.OtherResource
  ];

  readonly detail = signal<SeriesDetail | null>(null);
  readonly draftTypes = signal<Record<number, RpgMaterialType>>({});
  readonly selectedIds = signal<number[]>([]);
  readonly selectedVolumes = computed(() => {
    const selected = new Set(this.selectedIds());
    return this.volumes().filter(volume => selected.has(volume.id));
  });
  readonly canGroupSelected = computed(() => {
    const selected = this.selectedVolumes();
    const publicationTypes = new Set(selected
      .map(volume => volume.rpgMaterialType ?? RpgMaterialType.Unclassified)
      .filter(type => type !== RpgMaterialType.Unclassified));
    return selected.length >= 2 && publicationTypes.size <= 1 && !this.hasDraftChanges() && selected.every(volume => {
      const type = volume.rpgMaterialType ?? RpgMaterialType.Unclassified;
      return type === RpgMaterialType.Unclassified ||
        (type >= RpgMaterialType.CoreManual && type <= RpgMaterialType.OtherPublication);
    });
  });
  readonly bulkType = signal<RpgMaterialType>(RpgMaterialType.Unclassified);
  readonly isLoading = signal(true);
  readonly isSaving = signal(false);
  readonly isGrouping = signal(false);
  readonly showGroupingConfirmation = signal(false);
  readonly groupingError = signal('');
  readonly primaryVolumeId = signal<number | null>(null);
  readonly volumes = computed(() => this.detail()?.volumes ?? []);
  readonly unclassified = computed(() => this.volumes().filter(volume => this.typeFor(volume) === RpgMaterialType.Unclassified));
  readonly publications = computed(() => this.volumes().filter(volume => this.typeFor(volume) >= RpgMaterialType.CoreManual && this.typeFor(volume) <= RpgMaterialType.OtherPublication));
  readonly resources = computed(() => this.volumes().filter(volume => this.typeFor(volume) >= RpgMaterialType.Map));
  readonly hasDraftChanges = computed(() => this.volumes().some(volume => this.draftTypes()[volume.id] !== undefined && this.draftTypes()[volume.id] !== volume.rpgMaterialType));
  readonly unclassifiedCount = computed(() => this.volumes().filter(volume => this.typeFor(volume) === RpgMaterialType.Unclassified).length);
  readonly publicationCount = computed(() => this.volumes().filter(volume => this.typeFor(volume) >= RpgMaterialType.CoreManual && this.typeFor(volume) <= RpgMaterialType.OtherPublication).length);
  readonly resourceCount = computed(() => this.volumes().filter(volume => this.typeFor(volume) >= RpgMaterialType.Map).length);

  ngOnInit(): void {
    this.load();
  }

  typeFor(volume: Volume): RpgMaterialType {
    return this.draftTypes()[volume.id] ?? volume.rpgMaterialType ?? RpgMaterialType.Unclassified;
  }

  setType(volumeId: number, value: string | number): void {
    this.draftTypes.update(types => ({...types, [volumeId]: Number(value) as RpgMaterialType}));
  }

  setBulkType(value: string): void {
    this.bulkType.set(Number(value) as RpgMaterialType);
  }

  toggleSelection(volumeId: number, checked: boolean): void {
    this.cancelGrouping();
    this.selectedIds.update(ids => checked ? [...new Set([...ids, volumeId])] : ids.filter(id => id !== volumeId));
  }

  selectAll(checked: boolean): void {
    this.cancelGrouping();
    this.selectedIds.set(checked ? this.volumes().map(volume => volume.id) : []);
  }

  applyBulkType(): void {
    const selected = this.selectedIds();
    if (selected.length === 0) return;
    this.cancelGrouping();
    this.draftTypes.update(types => {
      const updated = {...types};
      for (const id of selected) updated[id] = this.bulkType();
      return updated;
    });
  }

  groupPrimaryLabel(volume: Volume): string {
    const links = [
      volume.rpgGeekId == null ? '' : `RPGGeek #${volume.rpgGeekId}`,
      volume.driveThruRpgId == null ? '' : `DriveThruRPG #${volume.driveThruRpgId}`
    ].filter(Boolean);
    return links.length > 0 ? `${volume.name} · ${links.join(' · ')}` : volume.name;
  }

  beginGrouping(): void {
    if (!this.canGroupSelected()) return;
    this.primaryVolumeId.set(this.selectedVolumes()[0]?.id ?? null);
    this.groupingError.set('');
    this.showGroupingConfirmation.set(true);
  }

  setPrimaryVolume(value: string): void {
    const id = Number(value);
    this.primaryVolumeId.set(Number.isInteger(id) && id > 0 ? id : null);
  }

  cancelGrouping(): void {
    this.showGroupingConfirmation.set(false);
    this.groupingError.set('');
    this.primaryVolumeId.set(null);
  }

  confirmGrouping(): void {
    const primaryVolumeId = this.primaryVolumeId();
    const volumeIds = this.selectedVolumes().map(volume => volume.id);
    if (!primaryVolumeId || volumeIds.length < 2 || !this.canGroupSelected() || this.isGrouping()) return;

    this.isGrouping.set(true);
    this.groupingError.set('');
    this.volumeService.groupRpgPublicationVersions(this.series().id, primaryVolumeId, volumeIds).subscribe({
      next: () => {
        this.selectedIds.set([]);
        this.draftTypes.set({});
        this.cancelGrouping();
        this.toastr.success(translate('rpg-review.group-success'));
        this.load();
      },
      error: error => {
        const message = error instanceof HttpErrorResponse && typeof error.error === 'string'
          ? error.error
          : translate('rpg-review.group-failed');
        this.groupingError.set(message);
        this.toastr.error(message);
        this.isGrouping.set(false);
      },
      complete: () => this.isGrouping.set(false)
    });
  }

  save(): void {
    const updates: RpgMaterialTypeUpdate[] = this.volumes()
      .filter(volume => this.draftTypes()[volume.id] !== undefined && this.draftTypes()[volume.id] !== volume.rpgMaterialType)
      .map(volume => ({volumeId: volume.id, materialType: this.draftTypes()[volume.id]}));
    if (updates.length === 0 || this.isSaving()) return;

    this.isSaving.set(true);
    this.volumeService.classifyRpgMaterialBatch(this.series().id, updates).subscribe({
      next: () => {
        this.draftTypes.set({});
        this.selectedIds.set([]);
        this.load();
        this.toastr.success(translate('rpg-review.saved'));
      },
      error: () => {
        this.toastr.error(translate('rpg-review.save-failed'));
        this.isSaving.set(false);
      },
      complete: () => this.isSaving.set(false)
    });
  }

  fileNames(volume: Volume): string[] {
    return volume.chapters.flatMap(chapter => chapter.files.map(file => file.filePath.split(/[\\/]/).pop() || file.filePath));
  }

  openActionLabel(volume: Volume): string {
    return this.typeFor(volume) >= RpgMaterialType.Map ? 'open-resource' : 'open-publication';
  }

  private load(): void {
    this.isLoading.set(true);
    this.seriesService.getSeriesDetail(this.series().id).subscribe({
      next: detail => {
        this.detail.set(detail);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }
}
