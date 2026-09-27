import {ChangeDetectionStrategy, Component, computed, inject, input, output, signal} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {TranslocoDirective, translate} from '@jsverse/transloco';
import {ToastrService} from '@openng/ngx-toastr';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Volume} from '../../_models/volume';
import {VolumeService} from '../../_services/volume.service';
import {UtcToLocalTimePipe} from '../../_pipes/utc-to-local-time.pipe';

@Component({
  selector: 'app-rpg-geek-metadata',
  imports: [TranslocoDirective, FormsModule, UtcToLocalTimePipe],
  templateUrl: './rpg-geek-metadata.component.html',
  styleUrl: './rpg-geek-metadata.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RpgGeekMetadataComponent {
  private readonly volumeService = inject(VolumeService);
  private readonly toastr = inject(ToastrService);

  volume = input.required<Volume>();
  libraryId = input.required<number>();
  seriesId = input.required<number>();
  seriesName = input<string>('');
  providerEnabled = input.required<boolean>();
  isAdmin = input.required<boolean>();

  readonly reload = output<void>();

  candidates = signal<Array<{id: number, name: string, yearPublished?: number | null}>>([]);
  searchQuery = signal('');
  manualId = signal<number | null>(null);
  loading = signal(false);
  searched = signal(false);

  statusLabel = computed(() =>
    translate('rpg-geek-metadata.status-' + (this.volume().rpgGeekMatchStatus ?? 0)));
  lastChecked = computed(() => this.volume().rpgGeekLastCheckedUtc ?? null);

  search(query: string | null = null) {
    const effectiveQuery = query ?? this.searchQuery();
    if (this.loading() || !effectiveQuery.trim()) return;
    this.loading.set(true);
    this.volumeService.getRpgGeekCandidates(this.volume().id, effectiveQuery.trim()).pipe(
      takeUntilDestroyed(),
    ).subscribe({
      next: results => {
        this.candidates.set(results);
        this.searched.set(true);
        this.loading.set(false);
      },
      error: () => {
        this.toastr.error(translate('rpg-geek-metadata.search-error'));
        this.loading.set(false);
      },
    });
  }

  searchBySeries() {
    const series = this.seriesName();
    if (series.trim()) {
      this.searchQuery.set(series.trim());
      this.search(series.trim());
    }
  }

  link(itemId: number | null) {
    const id = itemId ?? this.manualId();
    if (!id || id <= 0 || this.loading()) return;
    this.loading.set(true);
    this.volumeService.linkRpgGeek(this.volume().id, id).pipe(
      takeUntilDestroyed(),
    ).subscribe({
      next: () => {
        this.toastr.success(translate('rpg-geek-metadata.link-success'));
        this.loading.set(false);
        this.reload.emit();
      },
      error: () => {
        this.toastr.error(translate('rpg-geek-metadata.search-error'));
        this.loading.set(false);
      },
    });
  }

  refresh() {
    if (this.loading()) return;
    this.loading.set(true);
    this.volumeService.refreshRpgGeekMetadata(this.volume().id).pipe(
      takeUntilDestroyed(),
    ).subscribe({
      next: () => {
        this.toastr.info(translate('rpg-geek-metadata.refresh-queued'));
        this.loading.set(false);
        this.reload.emit();
      },
      error: () => {
        this.toastr.error(translate('rpg-geek-metadata.search-error'));
        this.loading.set(false);
      },
    });
  }
}
