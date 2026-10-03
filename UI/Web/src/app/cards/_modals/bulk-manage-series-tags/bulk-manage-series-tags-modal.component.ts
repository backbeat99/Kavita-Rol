import {ChangeDetectionStrategy, Component, computed, inject, input, signal} from '@angular/core';
import {NgbActiveModal} from '@ng-bootstrap/ng-bootstrap';
import {TranslocoDirective, translate} from '@jsverse/transloco';
import {EMPTY, catchError, finalize, take} from 'rxjs';
import {ToastrService} from '@openng/ngx-toastr';
import {Tag} from '../../../_models/tag';
import {SeriesService} from '../../../_services/series.service';
import {TypeaheadConfigFactoryService} from '../../../typeahead-config-factory.service';
import {TypeaheadComponent} from '../../../typeahead/_components/typeahead.component';

@Component({
  selector: 'app-bulk-manage-series-tags-modal',
  imports: [TranslocoDirective, TypeaheadComponent],
  templateUrl: './bulk-manage-series-tags-modal.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BulkManageSeriesTagsModalComponent {
  private readonly modal = inject(NgbActiveModal);
  private readonly seriesService = inject(SeriesService);
  private readonly typeaheadConfigFactory = inject(TypeaheadConfigFactoryService);
  private readonly toastr = inject(ToastrService);

  seriesIds = input<number[]>([]);
  protected readonly operation = signal<'add' | 'remove'>('add');
  protected readonly selectedTags = signal<Tag[]>([]);
  protected readonly isSaving = signal(false);
  protected readonly canSave = computed(() =>
    this.seriesIds().length > 0 && this.selectedTags().length > 0 && !this.isSaving()
  );
  protected readonly tagsTypeaheadSettings = computed(() =>
    this.typeaheadConfigFactory.forTag({id: 'bulk-series-tags', savedData: this.selectedTags()})
  );

  updateTags(tags: Tag[]): void {
    this.selectedTags.set(tags);
  }

  save(): void {
    if (!this.canSave()) return;

    const tagTitles = this.selectedTags()
      .map(tag => tag.title.trim())
      .filter(title => title.length > 0);

    this.isSaving.set(true);
    this.seriesService.bulkUpdateTags(this.seriesIds(), tagTitles, this.operation() === 'remove').pipe(
      take(1),
      catchError(() => {
        this.toastr.error(translate('errors.generic'));
        return EMPTY;
      }),
      finalize(() => this.isSaving.set(false))
    ).subscribe(() => {
      this.toastr.success(translate('bulk-manage-series-tags.saved', {count: this.seriesIds().length}));
      this.modal.close(true);
    });
  }

  close(): void {
    this.modal.dismiss();
  }
}
