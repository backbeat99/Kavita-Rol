import {ChangeDetectionStrategy, Component, computed, inject, input, OnDestroy, OnInit, output, signal} from '@angular/core';
import {CommonModule, DOCUMENT} from '@angular/common';
import {RouterLink} from '@angular/router';
import {TranslocoDirective, translate} from '@jsverse/transloco';
import {ToastrService} from '@openng/ngx-toastr';
import {finalize} from 'rxjs/operators';
import {AccountService} from '../_services/account.service';
import {ImageService} from '../_services/image.service';
import {ReaderService} from '../_services/reader.service';
import {SeriesService} from '../_services/series.service';
import {Library} from '../_models/library/library';
import {DriveThruRpgMatchStatus, RpgGeekMatchStatus, RpgMaterialType} from '../_models/rpg/rpg-catalog';
import {Series} from '../_models/series';
import {Volume} from '../_models/volume';

@Component({
  selector: 'app-rpg-game-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslocoDirective],
  templateUrl: './rpg-game-detail.component.html',
  styleUrl: './rpg-game-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RpgGameDetailComponent implements OnInit, OnDestroy {
  private readonly document = inject(DOCUMENT);
  private readonly accountService = inject(AccountService);
  protected readonly imageService = inject(ImageService);
  private readonly readerService = inject(ReaderService);
  private readonly seriesService = inject(SeriesService);
  private readonly toastr = inject(ToastrService);

  readonly library = input.required<Library>();
  readonly series = input.required<Series>();
  readonly volumes = input.required<Volume[]>();
  readonly coverImage = input<string>();
  readonly chooseCover = output<void>();
  readonly isAdmin = this.accountService.hasAdminRole;
  readonly isScanning = signal(false);
  readonly RpgMaterialType = RpgMaterialType;

  readonly publications = computed(() => this.volumes()
    .filter(volume => volume.rpgMaterialType >= RpgMaterialType.CoreManual && volume.rpgMaterialType <= RpgMaterialType.OtherPublication)
    .slice()
    .sort((a, b) => Number(b.rpgMaterialType === RpgMaterialType.CoreManual) - Number(a.rpgMaterialType === RpgMaterialType.CoreManual)
      || a.name.localeCompare(b.name)));

  readonly resources = computed(() => this.volumes()
    .filter(volume => volume.rpgMaterialType >= RpgMaterialType.Map)
    .slice()
    .sort((a, b) => a.name.localeCompare(b.name)));

  readonly unclassified = computed(() => this.volumes()
    .filter(volume => volume.rpgMaterialType === RpgMaterialType.Unclassified));

  // An unlinked publication is not automatically "pending": linking RPGGeek is optional.
  readonly rpgGeekToReview = computed(() => this.library().enableRpgGeekMetadata
    ? this.publications().filter(volume => !volume.rpgGeekId &&
      (volume.rpgGeekMatchStatus === RpgGeekMatchStatus.Candidate ||
        volume.rpgGeekMatchStatus === RpgGeekMatchStatus.Ambiguous))
    : []);

  readonly failedRpgGeekSearches = computed(() => this.library().enableRpgGeekMetadata
    ? this.publications().filter(volume => !volume.rpgGeekId && volume.rpgGeekMatchStatus === RpgGeekMatchStatus.Failed)
    : []);

  readonly driveThruRpgToReview = computed(() => this.library().enableDriveThruRpgMetadata
    ? this.publications().filter(volume => !volume.driveThruRpgId &&
      [DriveThruRpgMatchStatus.Candidate, DriveThruRpgMatchStatus.Ambiguous, DriveThruRpgMatchStatus.Failed]
        .includes(volume.driveThruRpgMatchStatus))
    : []);

  readonly hasReviewWork = computed(() => this.unclassified().length > 0 ||
    this.rpgGeekToReview().length > 0 || this.failedRpgGeekSearches().length > 0 || this.driveThruRpgToReview().length > 0);

  ngOnInit(): void {
    this.resetCompanionBarScroll();
    this.document.body?.classList.add('rpg-game-detail-active');
  }

  ngOnDestroy(): void {
    this.document.body?.classList.remove('rpg-game-detail-active');
  }

  private resetCompanionBarScroll(): void {
    const companionBar = this.document.querySelector<HTMLElement>('.companion-bar');
    if (companionBar) companionBar.scrollTop = 0;
  }

  materialLabel(volume: Volume): string {
    return translate('rpg-review.types.' + volume.rpgMaterialType);
  }

  fileFormats(volume: Volume): string {
    const formats = new Set(volume.chapters.flatMap(chapter => chapter.files)
      .map(file => file.filePath.split(/[\\/]/).pop() || '')
      .map(fileName => fileName.includes('.') ? fileName.split('.').pop()!.toUpperCase() : '')
      .filter(Boolean));
    return Array.from(formats).join(' · ');
  }

  read(volume: Volume): void {
    this.readerService.readVolume(this.library().id, this.series().id, volume);
  }

  scan(): void {
    if (this.isScanning()) return;
    this.isScanning.set(true);
    this.seriesService.scan(this.library().id, this.series().id).pipe(
      finalize(() => this.isScanning.set(false))
    ).subscribe({
      next: () => this.toastr.info(translate('toasts.scan-queued', {name: this.series().name})),
      error: () => this.toastr.error(translate('errors.generic'))
    });
  }

}
