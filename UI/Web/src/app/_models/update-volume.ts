import {IHasMetadataIds} from "./common/i-has-metadata-ids";
import {RpgMaterialType} from "./library/rpg-material-type";

export interface UpdateVolume extends IHasMetadataIds {
  driveThruRpgId?: number | null;
  rpgGeekId?: number | null;
  rpgMaterialType?: RpgMaterialType;
  name?: string;
  nameLocked?: boolean;
  summary?: string;
  summaryLocked?: boolean;
  releaseDate?: string | null;
  releaseDateLocked?: boolean;
  language?: string;
  languageLocked?: boolean;
}
