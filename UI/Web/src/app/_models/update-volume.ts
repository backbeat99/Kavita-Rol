import {IHasMetadataIds} from "./common/i-has-metadata-ids";

export interface RpgBibliographyUpdate {
  name: string;
  nameLocked: boolean;
  summary: string;
  summaryLocked: boolean;
  rpgPublicationYear: number | null;
  rpgPublicationYearLocked: boolean;
  rpgWriters: string[];
  rpgWritersLocked: boolean;
  rpgPublishers: string[];
  rpgPublishersLocked: boolean;
}

export interface UpdateVolume extends IHasMetadataIds {
  id: number;
  rpgBibliography?: RpgBibliographyUpdate;
}
