import {ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, inject, Input, OnInit, signal} from '@angular/core';
import {NgClass} from '@angular/common';
import {FormControl, FormGroup, FormsModule, ReactiveFormsModule} from "@angular/forms";
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
import {UtilityService} from "../../shared/_services/utility.service";
import {ImageService} from "../../_services/image.service";
import {UploadService} from "../../_services/upload.service";
import {AccountService} from "../../_services/account.service";
import {ActionService} from "../../_services/action.service";
import {DownloadService} from '../../shared/_services/download.service';
import {DownloadEntityType} from '../../shared/_models/download-queue-item';
import {LibraryType} from "../../_models/library/library";
import {PersonRole} from "../../_models/metadata/person";
import {concat, switchMap} from "rxjs";
import {takeUntilDestroyed} from "@angular/core/rxjs-interop";
import {MangaFile} from "../../_models/manga-file";
import {BreakpointService} from "../../_services/breakpoint.service";
import {ActionFactoryService} from "../../_services/action-factory.service";
import {ActionItem} from "../../_models/actionables/action-item";
import {Action} from "../../_models/actionables/action";
import {modalDeleted, modalSaved} from "../../_models/modal/modal-result";
import {VolumeService} from "../../_services/volume.service";
import {LibraryService} from "../../_services/library.service";
import {UpdateVolume} from "../../_models/update-volume";
import {Tabs} from "../../_models/tabs";
import {
  addMetadataIdControls,
  EditExternalMetadataFormComponent
} from "../../shared/_components/edit-external-metadata-form/edit-external-metadata-form.component";
import {EditModalShellComponent} from "../../shared/edit-modal-shell/edit-modal-shell.component";
import {EditTabDirective} from "../../shared/_directive/edit-tab.directive";
import {MangaFormat} from "../../_models/manga-format";
import {RpgMaterialType} from "../../_models/library/rpg-material-type";


@Component({
  selector: 'app-edit-volume-modal',
  imports: [
    FormsModule,
    NgClass,
    TranslocoDirective,
    ReactiveFormsModule,
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
    EditTabDirective
  ],
  templateUrl: './edit-volume-modal.component.html',
  styleUrl: './edit-volume-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EditVolumeModalComponent implements OnInit {
  public readonly modal = inject(NgbActiveModal);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly LibraryType = LibraryType;
  protected readonly RpgMaterialType = RpgMaterialType;
  protected readonly rpgMaterialOptions = Object.values(RpgMaterialType).filter(value => typeof value === 'number') as RpgMaterialType[];
  public readonly utilityService = inject(UtilityService);
  public readonly imageService = inject(ImageService);
  private readonly uploadService = inject(UploadService);
  private readonly cdRef = inject(ChangeDetectorRef);
  public readonly accountService = inject(AccountService);
  private readonly actionFactoryService = inject(ActionFactoryService);
  private readonly actionService = inject(ActionService);
  private readonly downloadService = inject(DownloadService);
  private readonly volumeService = inject(VolumeService);
  private readonly libraryService = inject(LibraryService);
  protected readonly breakpointService = inject(BreakpointService);
  private readonly coverChooserConfigFactory = inject(CoverChooserConfigFactoryService);

  @Input({required: true}) volume!: Volume;
  @Input({required: true}) libraryType!: LibraryType;
  @Input({required: true}) libraryId!: number;
  @Input({required: true}) seriesId!: number;

  activeId = Tabs.Info;
  editForm: FormGroup = new FormGroup({});
  selectedCover: string = '';
  coverImageReset = false;
  coverImageDirty = false;
  chooserConfig = signal<CoverImageChooserConfig>({});
  enableDriveThruRpgMetadata = signal(false);

  tasks = this.actionFactoryService.getActionablesForSettingsPage(this.actionFactoryService.getVolumeActions(this.seriesId, this.libraryId, this.libraryType), this.blacklist);
  /**
   * A copy of the chapter from init. This is used to compare values for name fields to see if lock was modified
   */
  initVolume!: Volume;
  size: number = 0;
  files: Array<MangaFile> = [];

  constructor() {
    if (!this.accountService.hasAdminRole()) {
      this.activeId = Tabs.Info;
      this.cdRef.markForCheck();
    }
  }

  get blacklist() {
    return [Action.Edit, Action.IncognitoRead, Action.AddToReadingList];
  }


  ngOnInit() {
    this.initVolume = Object.assign({}, this.volume);
    if (this.libraryType === LibraryType.Rpg && this.accountService.hasAdminRole()) {
      this.libraryService.getLibrary(this.libraryId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(library => {
        this.enableDriveThruRpgMetadata.set(library.enableDriveThruRpgMetadata ?? false);
      });
    }

    this.files = this.volume.chapters.flatMap(c => c.files);
    this.size = this.files.reduce((sum, v) => sum + v.bytes, 0);

    this.editForm.addControl('coverImageLocked', new FormControl(this.volume.coverImageLocked, []));
    if (this.libraryType === LibraryType.Rpg) {
      this.editForm.addControl('driveThruRpgId', new FormControl<number | null>(this.volume.driveThruRpgId ?? null));
      this.editForm.addControl('rpgGeekId', new FormControl<number | null>(this.volume.rpgGeekId ?? null));
      this.editForm.addControl('rpgMaterialType', new FormControl<RpgMaterialType>(this.volume.rpgMaterialType ?? RpgMaterialType.Unclassified));
      this.editForm.addControl('name', new FormControl<string>(this.volume.name));
      this.editForm.addControl('nameLocked', new FormControl<boolean>(this.volume.nameLocked ?? false));
      this.editForm.addControl('summary', new FormControl<string>(this.volume.summary ?? ''));
      this.editForm.addControl('summaryLocked', new FormControl<boolean>(this.volume.summaryLocked ?? false));
      this.editForm.addControl('releaseDate', new FormControl<string>(this.volume.releaseDate ? this.volume.releaseDate.slice(0, 10) : ''));
      this.editForm.addControl('releaseDateLocked', new FormControl<boolean>(this.volume.releaseDateLocked ?? false));
      this.editForm.addControl('language', new FormControl<string>(this.volume.language ?? ''));
      this.editForm.addControl('languageLocked', new FormControl<boolean>(this.volume.languageLocked ?? false));
    }
    addMetadataIdControls(this.editForm, this.volume);

    this.chooserConfig.set(this.coverChooserConfigFactory.forVolume(this.volume, this.libraryType));
  }

  close() {
    if (this.coverImageReset) {
      this.modal.close(modalSaved(this.volume, true));
    } else {
      this.modal.dismiss();
    }
  }

  save() {
    const model = this.editForm.getRawValue();
    if (this.libraryType === LibraryType.Rpg) {
      const productId = Number(model.driveThruRpgId);
      this.volume.driveThruRpgId = Number.isInteger(productId) && productId > 0 ? productId : null;
      const rpgGeekId = Number(model.rpgGeekId);
      this.volume.rpgGeekId = Number.isInteger(rpgGeekId) && rpgGeekId > 0 ? rpgGeekId : null;
      this.volume.rpgMaterialType = model.rpgMaterialType as RpgMaterialType;
      this.volume.name = model.name;
      this.volume.nameLocked = model.nameLocked;
      this.volume.summary = model.summary ?? '';
      this.volume.summaryLocked = model.summaryLocked;
      this.volume.releaseDate = model.releaseDate ? model.releaseDate + 'T00:00:00' : null;
      this.volume.releaseDateLocked = model.releaseDateLocked;
      this.volume.language = model.language ?? '';
      this.volume.languageLocked = model.languageLocked;
    }

    const updateData = {id: this.volume.id, ...model} as UpdateVolume;

    const apis = [
      this.volumeService.updateVolume(updateData)
    ];

    if (this.coverImageDirty) {
      apis.push(this.uploadService.updateVolumeCoverImage(this.volume.id, this.selectedCover, true));
    }

    concat(...apis).subscribe(() => {
      const needsCoverUpdate = this.coverImageDirty || this.coverImageReset;
      this.modal.close(modalSaved(this.volume, needsCoverUpdate));
    });
  }


  refreshDriveThruRpgMetadata() {
    const productId = Number(this.editForm.get('driveThruRpgId')?.value);
    this.volume.driveThruRpgId = Number.isInteger(productId) && productId > 0 ? productId : null;

    const apis = [this.volumeService.updateVolume({id: this.volume.id, ...this.editForm.getRawValue()} as UpdateVolume)];
    const needsCoverUpdate = this.coverImageDirty || this.coverImageReset;
    if (this.coverImageDirty) {
      apis.push(this.uploadService.updateVolumeCoverImage(this.volume.id, this.selectedCover, true));
    }

    concat(...apis).pipe(
      switchMap(() => this.volumeService.refreshDriveThruRpgMetadata(this.volume.id)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => {
      this.modal.close(modalSaved(this.volume, needsCoverUpdate));
    });
  }

  async runTask(action: ActionItem<Volume>) {
    switch (action.action) {
      case Action.MarkAsRead:
        this.actionService.markVolumeAsRead(this.seriesId, this.volume, (p) => {
          this.volume.pagesRead = p.pagesRead;
          this.cdRef.markForCheck();
        });
        break;
      case Action.MarkAsUnread:
        this.actionService.markVolumeAsUnread(this.seriesId, this.volume, (p) => {
          this.volume.pagesRead = 0;
          this.cdRef.markForCheck();
        });
        break;
      case Action.Delete:
        await this.actionService.deleteVolume(this.volume.id, (b) => {
          if (!b) return;
          this.modal.close(modalDeleted(this.volume));
        });
        break;
      case Action.Download:
        this.downloadService.download(DownloadEntityType.Volume, this.volume, this.libraryId, this.seriesId);
        break;
    }
  }

  handleCoverChanged(event: { isDirty: boolean; fileName: string }) {
    this.coverImageDirty = event.isDirty;
    this.selectedCover = event.fileName;
    this.cdRef.markForCheck();
  }

  handleReset() {
    this.coverImageReset = true;
    this.editForm.patchValue({ coverImageLocked: false });
    this.chooserConfig.set({ ...this.chooserConfig(), isLocked: false });
  }

  changeTab(tab?: Tabs) {
    if (!tab) return;
    this.activeId = tab;
    this.cdRef.markForCheck();
  }

  protected readonly Tabs = Tabs;
  protected readonly Action = Action;
  protected readonly PersonRole = PersonRole;
  protected readonly MangaFormat = MangaFormat;
}
