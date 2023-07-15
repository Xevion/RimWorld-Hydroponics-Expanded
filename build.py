import logging
import sys
from pathlib import Path
from shutil import rmtree, copytree
from typing import List

logger = logging.getLogger(__name__)
logging.basicConfig(level=logging.DEBUG)

PROJECT_DIRECTORY: Path = Path(__file__).parent


def main(output_directory: Path) -> None:
    # Check that output directory exists properly.
    if not output_directory.exists():
        # If the parent doesn't exist, we aren't going to create the folder.
        if not output_directory.parent.exists():
            raise RuntimeError("Cannot create more than one directory. Please check the output")

        output_directory.mkdir(parents=False, exist_ok=False)

    output_mod_directory: Path = output_directory / "HydroponicsExpanded"

    # Folders that we need to be removed from the output directory if they contain any files.
    pertinents_folders: List[str] = ["Defs", "Assemblies", "About", "Textures", "Languages"]
    if output_mod_directory.exists():
        logger.debug('Checking for folders to clear in destination.')

        for folder in pertinents_folders:
            destination_folder_path: Path = output_mod_directory / folder
            
            # Ignore folders that don't exist.
            if not destination_folder_path.exists():
                continue
                
            has_children: bool = any(destination_folder_path.iterdir())
            logger.debug('Clearing "{}" folder.'.format(folder))
            if has_children:
                rmtree(destination_folder_path)
            destination_folder_path.rmdir()
    else:
        output_mod_directory.mkdir(parents=False)

    logger.debug('Beginning folder copying sequence.')
    for folder in pertinents_folders:
        source_directory: Path = PROJECT_DIRECTORY / folder
        destination_directory: Path = output_mod_directory / folder

        logger.debug('Copying from "{}" to "{}'.format(source_directory, destination_directory))
        copytree(source_directory, destination_directory)


if __name__ == "__main__":
    try:
        output_directory: Path = PROJECT_DIRECTORY / "build"
        if len(sys.argv) > 1:
            output_directory = Path(sys.argv[1])
        else:
            logger.warning('Output directory automatically chosen.')

        logger.debug('Project Directory: {}'.format(PROJECT_DIRECTORY))
        logger.debug('Output Directory: {}'.format(output_directory))

        main(output_directory)
    except BaseException as e:
        logger.error("Build script failed", exc_info=e)
