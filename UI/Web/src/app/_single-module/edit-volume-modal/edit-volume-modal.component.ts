import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  untracked
} from '@angular/core';
import {form, FormField, max, maxLength, min, required} from '@angular/forms/signals';
import {NgbActiveModal} from "@ng-bootstrap/ng-bootstrap";
import {TranslocoDirective} from "@jsverse/transloco";
import {SettingItemComponent} from "../../settings/_components/setting-item/setting-item.component";
import {EntityTitleComponent} from "../../cards/entity-title/entity-title.component";
import {SettingButtonComponent} from "../../settings/_components/setting-button/setting-button.component";
import {CoverImageChooserComponent} from "../../cards/cover-image-chooser/cover-image-chooser.component";
import {
  CoverChooserConfigFactoryService,
  CoverImageChooserConfig
} from "../../_services/cover-chooser-config-factory.service";
import {CompactNumberPipe} from "../../_pipes/compact-number.pipe";
import {DefaultDatePipe} from "../../_pipes/default-date.pipe";
import {UtcToLocalTimePipe} from "../../_pipes/utc-to-local-time.pipe";
import {BytesPipe} from "../../_pipes/bytes.pipe";
import {ReadTimePipe} from "../../_pipes/read-time.pipe";
import {Volume} from "../../_models/volume";
import {RpgMaterialType} from "../../_models/rpg/rpg-catalog";
import {UtilityService} from "../../shared/_services/utility.service";
import {ImageService} from "../../_services/image.service";
import {UploadService} from "../../_services/upload.service";
import {AccountService} from "../../_services/account.service";
import {ActionService} from "../../_services/action.service";
import {DownloadService} from '../../shared/_services/download.service';
import {DownloadEntityType} from '../../shared/_models/download-queue-item';
import {LibraryType} from "../../_models/library/library";
import {PersonRole} from "../../_models/metadata/person";
import {finalize, map, of, switchMap} from "rxjs";
import {BreakpointService} from "../../_services/breakpoint.service";
import {ActionFactoryService} from "../../_services/action-factory.service";
import {ActionItem} from "../../_models/actionables/action-item";
import {Action} from "../../_models/actionables/action";
import {modalDeleted, modalSaved} from "../../_models/modal/modal-result";
import {VolumeService} from "../../_services/volume.service";
import {RpgBibliographyUpdate, UpdateVolumeRequest} from "../../_models/update-volume-request";
import {Tabs} from "../../_models/tabs";
import {
  applyExternalMetadataIdRules,
  EditExternalMetadataFormComponent
} from "../../shared/_components/edit-external-metadata-form/edit-external-metadata-form.component";
import {EditModalShellComponent} from "../../shared/edit-modal-shell/edit-modal-shell.component";
import {EditTabDirective} from "../../shared/_directive/edit-tab.directive";
import {FormFieldDirective} from "../../_directives/form-field.directive";
import {MangaFormat} from "../../_models/manga-format";
import {lockGroup} from "../../_helpers/field-lock";
import {LockableFieldComponent} from "../../shared/_components/lockable-field/lockable-field.component";

interface FormModel {
  coverImage: string;
  coverImageLocked: boolean;
  name: string;
  nameLocked: boolean;
  summary: string;
  summaryLocked: boolean;
  rpgPublicationYear: number | null;
  rpgPublicationYearLocked: boolean;
  rpgWriters: string;
  rpgWritersLocked: boolean;
  rpgPublishers: string;
  rpgPublishersLocked: boolean;
  aniListId: number;
  malId: number;
  hardcoverId: number;
  metronId: number;
  comicVineId: string | null;
  mangaBakaId: number;
  cbrId: number;
}

const blacklist = [Action.Edit, Action.IncognitoRead, Action.AddToReadingList];

@Component({
  selector: 'app-edit-volume-modal',
  imports: [
    TranslocoDirective,
    SettingItemComponent,
    EntityTitleComponent,
    SettingButtonComponent,
    CoverImageChooserComponent,
    CompactNumberPipe,
    DefaultDatePipe,
    UtcToLocalTimePipe,
    BytesPipe,
    ReadTimePipe,
    EditExternalMetadataFormComponent,
    EditModalShellComponent,
    EditTabDirective,
    FormField,
    FormFieldDirective,
    LockableFieldComponent
  ],
  templateUrl: './edit-volume-modal.component.html',
  styleUrl: './edit-volume-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EditVolumeModalComponent {
  public readonly modal = inject(NgbActiveModal);
  public readonly utilityService = inject(UtilityService);
  public readonly imageService = inject(ImageService);
  private readonly uploadService = inject(UploadService);
  public readonly accountService = inject(AccountService);
  private readonly actionFactoryService = inject(ActionFactoryService);
  private readonly actionService = inject(ActionService);
  private readonly downloadService = inject(DownloadService);
  private readonly volumeService = inject(VolumeService);
  protected readonly breakpointService = inject(BreakpointService);
  private readonly coverChooserConfigFactory = inject(CoverChooserConfigFactoryService);

  volume = model.required<Volume>();
  libraryType = input.required<LibraryType>();
  libraryId = input.required<number>();
  seriesId = input.required<number>();

  activeId = signal<Tabs>(Tabs.Info);
  isSaving = signal(false);
  saveError = signal(false);

  private selectedCover = '';
  private coverImageReset = false;
  private coverImageDirty = false;

  private readonly formModel = signal<FormModel>({
    coverImage: '',
    coverImageLocked: false,
    name: '',
    nameLocked: false,
    summary: '',
    summaryLocked: false,
    rpgPublicationYear: null,
    rpgPublicationYearLocked: false,
    rpgWriters: '',
    rpgWritersLocked: false,
    rpgPublishers: '',
    rpgPublishersLocked: false,
    aniListId: 0,
    malId: 0,
    hardcoverId: 0,
    metronId: 0,
    comicVineId: null,
    mangaBakaId: 0,
    cbrId: 0
  });
  formGroup = form(this.formModel, p => {
    applyExternalMetadataIdRules(p);
    required(p.name);
    maxLength(p.name, 500);
    maxLength(p.summary, 10000);
    min(p.rpgPublicationYear, 1000);
    max(p.rpgPublicationYear, 9999);
  });
  protected readonly locks = lockGroup(this.formGroup, () => this.volume(), [
    'name', 'summary', 'rpgPublicationYear', 'rpgWriters', 'rpgPublishers', 'coverImage',
  ]);
  protected readonly chooserConfig = computed<CoverImageChooserConfig>(() => ({
    ...this.coverChooserConfigFactory.forVolume(this.volume(), this.libraryType()),
    isLocked: this.locks.coverImage()
  }));

  tasks = computed(() => this.actionFactoryService.getActionablesForSettingsPage(
    this.actionFactoryService.getVolumeActions(this.seriesId(), this.libraryId(), this.libraryType()), blacklist));
  files = computed(() => this.volume().chapters.flatMap(chapter => chapter.files));
  size = computed(() => this.files().reduce((sum, file) => sum + file.bytes, 0));

  constructor() {
    effect(() => {
      untracked(() => this.formModel.set({
        coverImage: this.volume().coverImage ?? '',
        coverImageLocked: this.volume().coverImageLocked,
        name: this.volume().name,
        nameLocked: this.volume().nameLocked,
        summary: this.volume().summary ?? '',
        summaryLocked: this.volume().summaryLocked,
        rpgPublicationYear: this.volume().rpgPublicationYear,
        rpgPublicationYearLocked: this.volume().rpgPublicationYearLocked,
        rpgWriters: this.volume().rpgWriters?.join(', ') ?? '',
        rpgWritersLocked: this.volume().rpgWritersLocked,
        rpgPublishers: this.volume().rpgPublishers?.join(', ') ?? '',
        rpgPublishersLocked: this.volume().rpgPublishersLocked,
        aniListId: this.volume().aniListId,
        malId: this.volume().malId,
        hardcoverId: this.volume().hardcoverId,
        metronId: this.volume().metronId,
        comicVineId: this.volume().comicVineId,
        mangaBakaId: this.volume().mangaBakaId,
        cbrId: this.volume().cbrId,
      }));
      this.locks.coverImage.set(this.volume().coverImageLocked);
    });
  }

  close() {
    if (this.coverImageReset) {
      this.modal.close(modalSaved(this.volume(), true));
    } else {
      this.modal.dismiss();
    }
  }

  save() {
    if (this.isSaving() || this.formGroup().invalid()) return;

    const model = this.formModel();
    const bibliography: RpgBibliographyUpdate = {
      name: model.name.trim(),
      nameLocked: this.locks.name(),
      summary: model.summary,
      summaryLocked: this.locks.summary(),
      rpgPublicationYear: model.rpgPublicationYear,
      rpgPublicationYearLocked: this.locks.rpgPublicationYear(),
      rpgWriters: this.parseNames(model.rpgWriters),
      rpgWritersLocked: this.locks.rpgWriters(),
      rpgPublishers: this.parseNames(model.rpgPublishers),
      rpgPublishersLocked: this.locks.rpgPublishers(),
    };
    const updateData: UpdateVolumeRequest = {
      id: this.volume().id,
      aniListId: model.aniListId,
      malId: model.malId,
      hardcoverId: model.hardcoverId,
      metronId: model.metronId,
      comicVineId: model.comicVineId,
      mangaBakaId: model.mangaBakaId,
      cbrId: model.cbrId,
      coverImageLocked: this.locks.coverImage(),
      ...(this.libraryType() === LibraryType.Rpg ? {rpgBibliography: bibliography} : {}),
    };

    this.isSaving.set(true);
    this.saveError.set(false);
    this.volumeService.updateVolume(updateData).pipe(
      switchMap(volume => this.coverImageDirty
        ? this.uploadService.updateVolumeCoverImage(this.volume().id, this.selectedCover, true).pipe(map(() => volume))
        : of(volume)),
      finalize(() => this.isSaving.set(false))
    ).subscribe({
      next: volume => {
        this.volume.set(volume);
        const needsCoverUpdate = this.coverImageDirty || this.coverImageReset;
        this.modal.close(modalSaved(this.volume(), needsCoverUpdate));
      },
      error: () => this.saveError.set(true)
    });
  }

  private parseNames(value: string): string[] {
    return value.split(',').map(name => name.trim()).filter(Boolean);
  }

  async runTask(action: ActionItem<Volume>) {
    switch (action.action) {
      case Action.MarkAsRead:
        this.actionService.markVolumeAsRead(this.seriesId(), this.volume(), progress => {
          this.volume.update(current => ({...current, pagesRead: progress.pagesRead}));
        });
        break;
      case Action.MarkAsUnread:
        this.actionService.markVolumeAsUnread(this.seriesId(), this.volume(), () => {
          this.volume.update(current => ({...current, pagesRead: 0}));
        });
        break;
      case Action.Delete:
        await this.actionService.deleteVolume(this.volume().id, deleted => {
          if (deleted) this.modal.close(modalDeleted(this.volume()));
        });
        break;
      case Action.Download:
        this.downloadService.download(DownloadEntityType.Volume, this.volume(), this.libraryId(), this.seriesId());
        break;
    }
  }

  handleCoverChanged(event: {isDirty: boolean; fileName: string}) {
    this.coverImageDirty = event.isDirty;
    this.selectedCover = event.fileName;
  }

  handleReset() {
    this.coverImageReset = true;
    this.formModel.update(model => ({...model, coverImageLocked: false}));
    this.locks.coverImage.set(false);
  }

  changeTab(tab?: Tabs) {
    if (tab) this.activeId.set(tab);
  }

  protected readonly Tabs = Tabs;
  protected readonly Action = Action;
  protected readonly PersonRole = PersonRole;
  protected readonly MangaFormat = MangaFormat;
  protected readonly LibraryType = LibraryType;
  protected readonly RpgMaterialType = RpgMaterialType;
}
